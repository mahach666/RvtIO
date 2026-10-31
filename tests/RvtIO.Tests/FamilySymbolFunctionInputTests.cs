using System.Text;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class FamilySymbolFunctionInputTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(1, -1)]
    [InlineData(1, int.MinValue)] [InlineData(2, int.MaxValue)]
    public void ReadsOwnIntegerMemberAfterFixedIntPairs(int pairCount, int value)
    {
        byte[] block = Block(pairCount, value);
        var input = TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, Size(block), 1829);
        Assert.NotNull(input);
        Assert.Equal(500, input.OwnerId);
        Assert.Equal(value, input.Value);
        Assert.Equal(1, input.RecordIndex);
        Assert.Equal(2, input.RecordCount);
        Assert.True(input.ParamsStart >= 14);
        Assert.Equal(2 * (4 + 10 + 4 + 8 + 4 + 4 + 4 + 1 + 1) + 8,
            input.ParamsEnd - input.ParamsStart);
    }

    [Theory]
    [InlineData("footer")] [InlineData("header_size")] [InlineData("class")]
    [InlineData("owner")] [InlineData("zero_owner")] [InlineData("null_params")]
    [InlineData("external_params")] [InlineData("wrong_params")]
    [InlineData("bad_pairs")] [InlineData("bad_count")] [InlineData("short_record")]
    [InlineData("expression")] [InlineData("instance")] [InlineData("reporting")]
    [InlineData("missing")] [InlineData("geometry_map")]
    public void UnsupportedInputDoesNotBecomeAFalseDefault(string mutation)
    {
        byte[] block = Block(0, 1, mutation);
        Assert.Null(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, Size(block), 1829));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DuplicatePidKeepsTheFirstRecordIncludingItsBlock(bool blocked)
    {
        byte[] block = Block(0, 1, blocked ? "blocked_duplicate" : "duplicate");
        var input = TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, Size(block), 1829);
        if (blocked) Assert.Null(input);
        else { Assert.NotNull(input); Assert.Equal(-7, input.Value); Assert.Equal(0, input.RecordIndex); }
    }

    [Fact]
    public void InvalidBoundsAndOtherClassesFailClosed()
    {
        byte[] block = Block(0, 1);
        Assert.Null(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, -1, Size(block), 1829));
        Assert.Null(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, int.MaxValue, Size(block), 1829));
        Assert.Null(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, uint.MaxValue, 1829));
        Assert.Null(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, 0, 1829));
        Assert.Null(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, Size(block), 1794));
    }

    [Fact]
    public void NonzeroFrameStartKeepsFrameRelativePrefixBounds()
    {
        byte[] block = Block(2, 5);
        byte[] padded = new byte[block.Length + 40];
        block.CopyTo(padded, 16);
        Assert.Equal(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, Size(block), 1829),
            TfBlockDeserializer.ExtractFamilySymbolFunctionInput(padded, 16, Size(block), 1829));
    }

    [Fact]
    public void NewPrivateWalkDoesNotMutateSchemaOrOrdinaryParams()
    {
        byte[] block = Block(1, 1);
        var before = TfBlockDeserializer.ExtractParams(block, 0, Size(block), 1829);
        Assert.NotNull(TfBlockDeserializer.ExtractFamilySymbolFunctionInput(block, 0, Size(block), 1829));
        Assert.Equal(1763, TfBlockDeserializer.SerializationFields(1763)!.Single(f => f.Name == "second").Ref);
        Assert.Equal(before?.Count, TfBlockDeserializer.ExtractParams(block, 0, Size(block), 1829)?.Count);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void OrdinaryParamsWalkReadsTheEightByteFaceTagPairsOfASymbolRoot(int pairs)
    {
        // m_dummyHostInsertionFaceTags is a counted list of int[2]: a door or window symbol with such records used to lose
        // every field after them in the ordinary walk (24 bytes per record), and with it the symbol's named parameters.
        byte[] block = Block(pairs, 1);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(block, 0, Size(block), 1829));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 5)]
    public void RecordedFunctionOfAnOwnSymbolRootIsKeptForTheElement(int pairs, int value)
    {
        byte[] block = Block(pairs, value);
        var state = new Internal.ParsingState();
        RvtFileReader.RecordOwnFamilyCutoutInputs(state, block, 0, Size(block), 1829, 500);
        Assert.Equal((1, (int?)value), state.FamilySymbolFunctions[500]);
    }

    [Theory]
    [InlineData(-1)] [InlineData(6)] [InlineData(int.MaxValue)]
    public void ValueOutsideTheWallFunctionRangeIsNotAFunction(int value)
    {
        byte[] block = Block(0, value);
        var state = new Internal.ParsingState();
        RvtFileReader.RecordOwnFamilyCutoutInputs(state, block, 0, Size(block), 1829, 500);
        Assert.Equal((1, (int?)null), state.FamilySymbolFunctions[500]);
    }

    [Fact]
    public void ForeignOwnerOrUnreadableRootRecordsNoFunction_AndARepeatedRootClearsIt()
    {
        byte[] block = Block(0, 1);
        var state = new Internal.ParsingState();
        RvtFileReader.RecordOwnFamilyCutoutInputs(state, block, 0, Size(block), 1829, 501);
        Assert.Equal((1, (int?)null), state.FamilySymbolFunctions[501]);
        byte[] expression = Block(0, 1, "expression");
        RvtFileReader.RecordOwnFamilyCutoutInputs(state, expression, 0, Size(expression), 1829, 500);
        Assert.Equal((1, (int?)null), state.FamilySymbolFunctions[500]);

        state = new Internal.ParsingState();
        RvtFileReader.RecordOwnFamilyCutoutInputs(state, block, 0, Size(block), 1829, 500);
        RvtFileReader.RecordOwnFamilyCutoutInputs(state, block, 0, Size(block), 1829, 500);
        Assert.Equal((2, (int?)null), state.FamilySymbolFunctions[500]);
        RvtFileReader.RecordOwnFamilyCutoutInputs(state, block, 0, Size(block), 1794, 500);
        Assert.Equal(2, state.FamilySymbolFunctions[500].BlockCount);
    }

    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);

    private static byte[] Block(int pairs, int value, string? mutation = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(500); writer.Write(0u); writer.Write(0u); writer.Write((ushort)1829);
        foreach (var field in TfBlockDeserializer.SerializationFields(1829)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(500); continue; }
            if (field.Declaring == 1829 && field.Name == "m_pParams")
            {
                if (mutation == "null_params") writer.Write(0u);
                else { writer.Write(uint.MaxValue); writer.Write(mutation == "external_params" ? (ushort)0
                    : mutation == "wrong_params" ? (ushort)713 : (ushort)714); }
                continue;
            }
            if (field.Declaring == 1829 && field.Name == "m_dummyHostInsertionFaceTags")
            {
                writer.Write(mutation == "bad_pairs" ? 1_000_001u : (uint)pairs);
                for (int i = 0; i < pairs; i++) { writer.Write(123 + i); writer.Write(-456 - i); }
                continue;
            }
            if (field.Declaring == 1829 && field.Name == "m_subInstData")
            {
                writer.Write(1u); writer.Write(42);
                foreach (var nested in TfBlockDeserializer.SerializationFields(1724)!) Default(nested);
                continue;
            }
            Default(field);
        }
        writer.Write(mutation == "bad_count" ? 100_001u : 2u);
        bool duplicate = mutation is "duplicate" or "blocked_duplicate";
        Record(duplicate ? -1001006 : 777, -7, mutation == "blocked_duplicate");
        if (mutation == "short_record") writer.Write(999u);
        else Record(mutation == "missing" ? 888 : -1001006, value, mutation == "expression");
        writer.Write(mutation == "geometry_map" ? 1u : 0u);
        writer.Write(0u);
        byte[] block = stream.ToArray(); uint size = (uint)block.Length - 16;
        BitConverter.GetBytes(size).CopyTo(block, 8); BitConverter.GetBytes(size).CopyTo(block, block.Length - 4);
        if (mutation == "footer") block[^1] ^= 1;
        if (mutation == "header_size") block[8] ^= 1;
        if (mutation == "class") block[12] ^= 1;
        if (mutation == "owner") block[0] ^= 1;
        if (mutation == "zero_owner") BitConverter.GetBytes(0).CopyTo(block, 0);
        return block;

        void Record(int pid, int integer, bool expression)
        {
            writer.Write(5u); writer.Write(Encoding.Unicode.GetBytes("Wrong"));
            if (expression) { writer.Write(uint.MaxValue); writer.Write((ushort)0); } else writer.Write(0u);
            writer.Write(99d); writer.Write(123); writer.Write(pid); writer.Write(integer);
            writer.Write(mutation == "instance" ? (byte)1 : (byte)0);
            writer.Write(mutation == "reporting" ? (byte)1 : (byte)0);
        }
        void Default(TfBlockDeserializer.TfField field)
        {
            if (field.Declaring == 297 && field.Name == "m_minmax") { writer.Write(new byte[48]); return; }
            if (field.Mods >> 4 == 5) { writer.Write(0u); return; }
            int n = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int i = 0; i < n; i++) switch (field.Type)
            {
                case 1: case 2: case 3: writer.Write((byte)0); break;
                case 4: case 5: case 6: writer.Write(0); break;
                case 7: writer.Write(0d); break;
                case 8: writer.Write(0u); break;
                case 9: writer.Write(new byte[16]); break;
                case 10: writer.Write(0L); break;
                case 13: writer.Write(new byte[(field.Cnt ?? 1) * 8]); break;
                case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                    foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) Default(nested); break;
                case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(field.Name);
            }
        }
    }
}
