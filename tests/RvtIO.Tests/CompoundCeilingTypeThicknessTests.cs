using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CompoundCeilingTypeThicknessTests
{
    private static RawCompoundStructureLayer Layer(double width) =>
        new(BitConverter.DoubleToInt64Bits(width), 4, -1, -1, -1, 1, 0);

    private static (byte[] Block, uint Size) Block(double[]? widths = null, byte thick = 1,
        ushort? compound = 737, ushort? vertical = null, int? count = null,
        bool truncate = false, bool extra = false, uint serializedOwner = 501,
        bool includeStringStore = false, bool includeCell = false) =>
        CeilingCompoundStructureLayerParsingTests.CreateCompoundCeilingTypeBlock(
            501, serializedOwner, compound, (widths ?? [0.25, 0.125]).Select(Layer).ToArray(),
            truncateCompoundTail: truncate, declaredLayerCount: count,
            includeStringParameterStore: includeStringStore, includeCellList: includeCell,
            appendUnownedTailByte: extra, isThick: thick, verticalPointerClassId: vertical);

    [Theory]
    [InlineData(0.375, new double[] { 0.25, 0.125 })]
    [InlineData(0, new double[] { })]
    [InlineData(0, new double[] { -0.0 })]
    [InlineData(-1, new double[] { -1.25, 0.25 })]
    [InlineData(0, new double[] { 1e16, 1, -1e16 })]
    [InlineData(1, new double[] { 1e16, -1e16, 1 })]
    public void Width_UsesOriginalOrderedBinary64Addition(double expected, double[] widths)
    {
        var (block, size) = Block(widths);
        Assert.True(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(block, 0, size, 646, out double actual));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }

    [Theory]
    [InlineData(0)] [InlineData(2)] [InlineData(255)]
    public void NotThickOrNoncanonicalBoolean_IsNotAComputedThickness(byte thick)
    {
        var (block, size) = Block(thick: thick);
        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(block, 0, size, 646, out _));
    }

    [Theory]
    [InlineData(null)] [InlineData(-1)] [InlineData(0)]
    public void NullStructureOrNullAndEmptyLayers_ReturnPositiveZero(int? count)
    {
        var (block, size) = Block([], compound: count == null ? null : (ushort)737, count: count);
        Assert.True(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(block, 0, size, 646, out double value));
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(value));
        if (count == -1)
            Assert.False(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, size, 646, out _)); // old contract unchanged
    }

    [Theory]
    [InlineData(4099)] [InlineData(0)] [InlineData(737)]
    public void AnyNonNullVerticalPointer_IsNeverMistakenForAPlainLayerSum(ushort cls)
    {
        var (block, size) = Block(vertical: cls);
        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(block, 0, size, 646, out _));
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void NonfiniteInputOrSum_IsRejected(double width)
    {
        var (block, size) = Block([width, width]);
        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(block, 0, size, 646, out _));
    }

    [Theory]
    [InlineData("owner")] [InlineData("negative_owner")] [InlineData("class")]
    [InlineData("footer")] [InlineData("size")] [InlineData("tail")]
    [InlineData("truncated_structure")] [InlineData("truncated_layer")]
    [InlineData("huge_count")] [InlineData("wrong_pointer")]
    public void InvalidOwnFramingOrNamedStructure_FailsClosed(string mode)
    {
        var (block, size) = Block(serializedOwner: mode == "owner" ? 502u : mode == "negative_owner" ? uint.MaxValue : 501,
            compound: mode == "wrong_pointer" ? (ushort)738 : (ushort)737,
            truncate: mode == "truncated_structure", extra: mode == "tail",
            count: mode == "truncated_layer" ? 3 : mode == "huge_count" ? int.MaxValue : null);
        if (mode == "class") block[12] ^= 1;
        if (mode == "footer") block[^1] ^= 1;
        if (mode == "size") block[8] ^= 1;
        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(block, 0, size, 646, out _));
    }

    [Fact]
    public void ValidOwnDeferredPrefixAndNonzeroBlockStart_AreSupported()
    {
        var (block, size) = Block(includeStringStore: true, includeCell: true);
        byte[] padded = new byte[block.Length + 20]; block.CopyTo(padded, 9);
        Assert.True(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(padded, 9, size, 646, out double value));
        Assert.Equal(0.375, value);
        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(padded, -1, size, 646, out _));
        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(padded, 9, uint.MaxValue, 646, out _));
    }

    private static ParsingState State()
    {
        var state = new ParsingState();
        var (block, size) = Block();
        RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0, size, 646, 501);
        state.SystemTypeRoots[501] = (1, 646, null);
        return state;
    }

    [Fact]
    public void Builder_AddsOnlyPrivateValidationAndClearsItWithoutChangingPublicData()
    {
        var type = new Element { Id = 501, CategoryId = -2000038, ValidatedCeilingTypeThicknessFeet = 99 };
        type.Parameters.Add(new Parameter { Id = -1002206, StorageType = StorageType.Double, Value = -1 });
        type.Layers = [new MaterialLayer { ThicknessMm = 999 }];
        string before = JsonSerializer.Serialize(type);
        Assert.Equal(1, ElementBuilder.ApplyCompoundCeilingTypeThickness([type], State()));
        Assert.Equal(0.375, type.ValidatedCeilingTypeThicknessFeet);
        Assert.Equal(before, JsonSerializer.Serialize(type));
        Assert.Equal(0, ElementBuilder.ApplyCompoundCeilingTypeThickness([type], new ParsingState()));
        Assert.Null(type.ValidatedCeilingTypeThicknessFeet);
        Assert.Equal(before, JsonSerializer.Serialize(type));
    }

    [Theory]
    [InlineData("duplicate_host")] [InlineData("duplicate_type")] [InlineData("wrong_host")]
    [InlineData("wrong_type")] [InlineData("missing_host")] [InlineData("missing_type")]
    [InlineData("missing_value")] [InlineData("nonfinite_value")] [InlineData("wrong_category")]
    [InlineData("block_only")] [InlineData("duplicate_element")] [InlineData("nonpositive_id")]
    public void Builder_RequiresAllUniqueOwnTypeGuards(string mode)
    {
        var state = State();
        var type = new Element { Id = 501, CategoryId = -2000038, ValidatedCeilingTypeThicknessFeet = 99 };
        switch (mode)
        {
            case "duplicate_host": state.HostTypeCompoundStructures[501] = (2, 646, [], null); break;
            case "duplicate_type": state.SystemTypeRoots[501] = (2, 646, null); break;
            case "wrong_host": state.HostTypeCompoundStructures[501] = (1, 105, [], null); break;
            case "wrong_type": state.SystemTypeRoots[501] = (1, 3607, null); break;
            case "missing_host": state.HostTypeCompoundStructures.Clear(); break;
            case "missing_type": state.SystemTypeRoots.Clear(); break;
            case "missing_value": state.CompoundCeilingTypeThicknessFeet.Clear(); break;
            case "nonfinite_value": state.CompoundCeilingTypeThicknessFeet[501] = double.NaN; break;
            case "wrong_category": type.CategoryId = -2000011; break;
            case "block_only": type.IsBlockOnly = true; break;
            case "nonpositive_id": type.Id = -1; break;
        }
        Element[] elements = mode == "duplicate_element" ? [type, new Element { Id = 501 }] : [type];
        Assert.Equal(0, ElementBuilder.ApplyCompoundCeilingTypeThickness(elements, state));
        Assert.Null(type.ValidatedCeilingTypeThicknessFeet);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EveryDuplicateHostRoot_InvalidatesThicknessInBothOrders(bool malformedFirst)
    {
        var (good, size) = Block(); byte[] bad = (byte[])good.Clone(); bad[^1] ^= 1;
        var state = new ParsingState();
        foreach (byte[] block in malformedFirst ? new[] { bad, good } : new[] { good, bad })
            RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0, size, 646, 501);
        Assert.Equal(2, state.HostTypeCompoundStructures[501].BlockCount);
        Assert.Null(state.CompoundCeilingTypeThicknessFeet[501]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnsupportedHostRoot_AlsoInvalidatesThicknessInBothOrders(bool unsupportedFirst)
    {
        var (block, size) = Block(); var state = new ParsingState();
        foreach (ushort cls in unsupportedFirst ? new ushort[] { 105, 646 } : new ushort[] { 646, 105 })
            RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0, size, cls, 501);
        Assert.Equal(2, state.HostTypeCompoundStructures[501].BlockCount);
        Assert.Null(state.CompoundCeilingTypeThicknessFeet[501]);
    }

    [Theory]
    [InlineData("later_cell", true)] [InlineData("broken_earlier_cell", false)]
    public void ExactNamedStructurePrefix_DoesNotSkipAnEarlierFIFOFailure(string mode, bool expected)
    {
        byte[] block = HostTypeLayerSetNameTests.Block(646, mode, isThick: 1);
        bool accepted = TfBlockDeserializer.TryExtractCompoundCeilingTypeThicknessFeet(block, 0,
            BitConverter.ToUInt32(block, 8), 646, out double value);
        Assert.Equal(expected, accepted);
        if (expected) Assert.Equal(0.375, value);
    }
}
