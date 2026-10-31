using System.Text;
using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CurtainWallTypeNameSerializationTests
{
    private const string OwnName = "Витраж_В13*,Импосты 50х200";

    [Fact]
    public void OwnSymbolInfoName_IsNotAParameterMarkOrPublicName()
    {
        byte[] block = Block();
        Assert.Equal(OwnName, Decode(block));
        var state = new ParsingState();
        Record(state, block);
        var type = new Element { Id = 100, CategoryId = -2000011, Name = "В 13*" };
        ElementBuilder.ApplyCurtainTypeNames([type], state);
        Assert.Equal(OwnName, type.SerializedCurtainWallTypeName);
        Assert.Equal("В 13*", type.Name);
        Assert.DoesNotContain("SerializedCurtainWallTypeName", JsonSerializer.Serialize(type));
    }

    [Theory]
    [InlineData("footer")] [InlineData("header_size")] [InlineData("class")]
    [InlineData("owner")] [InlineData("zero_owner")] [InlineData("truncated_root")]
    [InlineData("truncated_name")] [InlineData("length_overflow")] [InlineData("empty")]
    [InlineData("whitespace")] [InlineData("nul")] [InlineData("surrogate")]
    [InlineData("external_pointer")] [InlineData("null_pointer")]
    [InlineData("wrong_class")] [InlineData("unrelated_pointer")]
    [InlineData("broken_prior_store")]
    public void MalformedOrUnownedName_FailsClosed(string mutation)
    {
        foreach (ushort classId in new ushort[] { 2559, 512 })
        {
            byte[] block = Block(mutation, classId);
            if (mutation == "footer") block[^1] ^= 1;
            if (mutation == "header_size") BitConverter.GetBytes(Size(block) - 1).CopyTo(block, 8);
            if (mutation == "class") BitConverter.GetBytes((ushort)914).CopyTo(block, 12);
            if (mutation == "owner") BitConverter.GetBytes(101).CopyTo(block, 0);
            if (mutation == "zero_owner") BitConverter.GetBytes(0).CopyTo(block, 0);
            Assert.Null(classId == 512 ? TfBlockDeserializer.ExtractBasicWallTypeName(block, 0, Size(block), classId) : Decode(block));
        }
    }

    [Fact]
    public void BoundsAreChecked_AndACompleteNameDoesNotRequireUnrelatedLaterGeometry()
    {
        byte[] block = Block();
        Assert.Null(TfBlockDeserializer.ExtractNewCurtainWallTypeName(block, -1, Size(block), 2559));
        Assert.Null(TfBlockDeserializer.ExtractNewCurtainWallTypeName(block, int.MaxValue, Size(block), 2559));
        Assert.Null(TfBlockDeserializer.ExtractNewCurtainWallTypeName(block, 0, uint.MaxValue, 2559));
        Assert.Equal(OwnName, Decode(Block("bad_later_geometry")));
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("malformed_first")]
    [InlineData("other_wall_root")] [InlineData("missing")]
    [InlineData("wrong_category")] [InlineData("wrong_owner")]
    public void AmbiguousOrMissingOwner_ClearsStaleName(string mutation)
    {
        var state = new ParsingState(); byte[] block = Block();
        if (mutation == "malformed_first") Record(state, Block("null_pointer"));
        if (mutation == "other_wall_root")
            RvtFileReader.RecordNewCurtainWallTypeFunction(state, block, 0, Size(block), 512, 100);
        Record(state, block);
        if (mutation == "duplicate") Record(state, block);
        if (mutation == "missing") state.NewCurtainWallTypeNames.Clear();
        var type = new Element { Id = mutation == "wrong_owner" ? 101 : 100,
            CategoryId = mutation == "wrong_category" ? -2003500 : -2000011,
            SerializedCurtainWallTypeName = "stale" };
        ElementBuilder.ApplyCurtainTypeNames([type], state);
        Assert.Null(type.SerializedCurtainWallTypeName);
    }

    [Fact]
    public void WalkerOwnerMustMatchHeader()
    {
        var state = new ParsingState(); byte[] block = Block();
        RvtFileReader.RecordNewCurtainWallTypeName(state, block, 0, Size(block), 2559, 101);
        Assert.Empty(state.NewCurtainWallTypeNames);
    }

    private static void Record(ParsingState state, byte[] block)
    {
        RvtFileReader.RecordNewCurtainWallTypeFunction(state, block, 0, Size(block), 2559, 100);
        RvtFileReader.RecordNewCurtainWallTypeName(state, block, 0, Size(block), 2559, 100);
    }
    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);
    private static string? Decode(byte[] block) =>
        TfBlockDeserializer.ExtractNewCurtainWallTypeName(block, 0, Size(block), 2559);

    [Theory]
    [InlineData("valid")] [InlineData("later_geometry")] [InlineData("duplicate")]
    [InlineData("mixed_root")] [InlineData("malformed_first")] [InlineData("wrong_category")]
    [InlineData("wrong_owner")] [InlineData("missing")]
    public void BasicWallTypeName_RequiresItsOwnUniqueRoot(string mode)
    {
        byte[] block = Block(mode == "later_geometry" ? "bad_later_geometry" : null, 512);
        Assert.Equal(OwnName, TfBlockDeserializer.ExtractBasicWallTypeName(block, 0, Size(block), 512));
        Assert.Null(TfBlockDeserializer.ExtractNewCurtainWallTypeName(block, 0, Size(block), 512));
        var state = new ParsingState();
        uint owner = mode == "wrong_owner" ? 101u : 100u;
        if (mode == "malformed_first")
        {
            byte[] malformed = Block("null_pointer", 512);
            RvtFileReader.RecordBasicWallTypeFunction(state, malformed, 0, Size(malformed), 512, owner);
        }
        RvtFileReader.RecordBasicWallTypeFunction(state, block, 0, Size(block), 512, owner);
        RvtFileReader.RecordBasicWallTypeName(state, block, 0, Size(block), 512, owner);
        if (mode is "duplicate" or "mixed_root")
            RvtFileReader.RecordBasicWallTypeFunction(state, block, 0, Size(block), mode == "duplicate" ? (ushort)512 : (ushort)2559, owner);
        if (mode == "missing") state.BasicWallTypeNames.Clear();
        var type = new Element { Id = (int)owner, CategoryId = mode == "wrong_category" ? -2003500 : -2000011,
            Name = "mark", SerializedBasicWallTypeName = "stale" };
        ElementBuilder.ApplyBasicWallTypeNames([type], state);
        Assert.Equal(mode is "valid" or "later_geometry" ? OwnName : null, type.SerializedBasicWallTypeName);
        Assert.Equal("mark", type.Name);
        Assert.DoesNotContain("SerializedBasicWallTypeName", JsonSerializer.Serialize(type));
    }

    private static byte[] Block(string? mutation = null, ushort classId = 2559)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(100u); writer.Write(0u); writer.Write(0u); writer.Write(classId);
        var deferred = new List<ushort>();
        foreach (var field in TfBlockDeserializer.SerializationFields(classId)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(100); continue; }
            if (field.Name == "m_function") { writer.Write(1); continue; }
            if (field.Name == "m_bordMullions") { writer.Write(new byte[16]); continue; }
            if (field.Name == "m_pParamValueSetAString") { Pointer(0xB46); continue; }
            if (field.Name == "m_symbolInfo")
            {
                if (mutation is "null_pointer" or "unrelated_pointer") writer.Write(0u);
                else Pointer(mutation == "external_pointer" ? (ushort)0 : mutation == "wrong_class" ? (ushort)3904 : (ushort)3905);
                if (mutation == "truncated_root") break;
                continue;
            }
            if (field.Name == "m_pCompoundStructure" && mutation is "unrelated_pointer" or "bad_later_geometry")
            { Pointer(mutation == "unrelated_pointer" ? (ushort)3905 : (ushort)1589); continue; }
            WriteDefault(field);
        }
        foreach (ushort cls in deferred)
        {
            if (mutation == "truncated_root") break;
            if (cls == 0xB46)
            {
                writer.Write(mutation == "broken_prior_store" ? uint.MaxValue : 1u);
                if (mutation == "broken_prior_store") break;
                writer.Write(11070); WriteString("В 13*");
            }
            else if (cls == 3905)
            {
                if (mutation == "length_overflow") writer.Write(uint.MaxValue);
                else if (mutation == "truncated_name") { writer.Write(10u); writer.Write((ushort)'A'); }
                else if (mutation == "surrogate") { writer.Write(1u); writer.Write((ushort)0xD800); }
                else WriteString(mutation switch { "empty" => "", "whitespace" => "  ", "nul" => "a\0b", _ => OwnName });
            }
            else { writer.Write((byte)255); break; }
        }
        writer.Write(0u);
        byte[] block = stream.ToArray(); uint size = (uint)block.Length - 16;
        BitConverter.GetBytes(size).CopyTo(block, 8); BitConverter.GetBytes(size).CopyTo(block, block.Length - 4);
        return block;

        void Pointer(ushort cls) { writer.Write(uint.MaxValue); writer.Write(cls); if (cls > 0) deferred.Add(cls); }
        void WriteString(string value) { writer.Write((uint)value.Length); writer.Write(Encoding.Unicode.GetBytes(value)); }
        void WriteDefault(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 == 5) { writer.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int index = 0; index < count; index++)
                switch (field.Type)
                {
                    case 1: case 2: writer.Write((byte)0); break;
                    case 4: writer.Write(0); break;
                    case 7: writer.Write(0d); break;
                    case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                        foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) WriteDefault(nested);
                        break;
                    case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                    default: throw new InvalidOperationException(field.Name);
                }
        }
    }
}
