using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Ifc;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class BasicWallTypeThicknessTests
{
    private static RawCompoundStructureLayer Layer(double width) =>
        new(BitConverter.DoubleToInt64Bits(width), 4, -1, -1, -1, 0, 0);

    [Theory]
    [InlineData(0.375, new double[] { 0.25, 0.125 })]
    [InlineData(0, new double[] { })]
    [InlineData(0, new double[] { -0.0 })]
    [InlineData(-1, new double[] { -1.25, 0.25 })]
    [InlineData(0, new double[] { 1e16, 1, -1e16 })]
    [InlineData(1, new double[] { 1e16, -1e16, 1 })]
    public void OwnBasicWallUsesTheSharedOriginalOrderedGetter(double expected, double[] widths)
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512, layers: widths.Select(Layer).ToArray());
        Assert.True(Read(block, 512, out double actual));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void NonfiniteInputAndOverflowAreNotAWidth(double value)
        => Assert.False(Read(HostTypeLayerSetNameTests.Block(512, layers: [Layer(value), Layer(value)]), 512, out _));

    [Theory]
    [InlineData("owner")] [InlineData("class")] [InlineData("footer")]
    [InlineData("tail")] [InlineData("size")] [InlineData("vertical")]
    [InlineData("wrong_vertical_class")] [InlineData("broken_earlier_cell")]
    public void InvalidFrameOrUnconfirmedStructureIsRejected(string mode)
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512, mode,
            vertical: mode is "vertical" or "wrong_vertical_class" ? [] : null);
        if (mode == "owner") block[0] ^= 1;
        if (mode == "class") block[12] ^= 1;
        if (mode == "footer") block[^1] ^= 1;
        if (mode == "size") block[8] ^= 1;
        Assert.False(Read(block, 512, out _));
    }

    [Theory]
    [InlineData(646)] [InlineData(1915)] [InlineData(513)]
    public void OtherHostClassesAreNotBasicWalls(int cls)
        => Assert.False(Read(HostTypeLayerSetNameTests.Block((ushort)cls), (ushort)cls, out _));

    [Fact]
    public void NullStructureRequiresCompleteOwnFifoAndReturnsPositiveZero()
    {
        Assert.True(Read(HostTypeLayerSetNameTests.Block(512, "missing_pointer"), 512, out double width));
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(width));
    }

    [Fact]
    public void NonzeroStartAndLaterUnrelatedObjectsDoNotChangeTheBoundedGetter()
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512, "later_cell"), segment = new byte[block.Length+19];
        block.CopyTo(segment, 9);
        Assert.True(TfBlockDeserializer.TryExtractBasicWallTypeThicknessFeet(segment, 9,
            BitConverter.ToUInt32(block, 8), 512, out double width));
        Assert.Equal(0.375, width);
        Assert.False(TfBlockDeserializer.TryExtractBasicWallTypeThicknessFeet(segment, -1, uint.MaxValue, 512, out _));
    }

    private static bool Read(byte[] data, ushort cls, out double width)
        => TfBlockDeserializer.TryExtractBasicWallTypeThicknessFeet(data, 0, BitConverter.ToUInt32(data, 8), cls, out width);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SweptRectangleUsesOwnWidthWithOrWithoutLegacyLayers(bool legacyLayers)
    {
        var wall = new Element { ValidatedWallTypeThicknessFeet = 0.25,
            Layers = legacyLayers ? [new MaterialLayer { ThicknessMm = 1 }] : null,
            Location = new LocationCurve { CurveType = "Line", StartPoint = [0, 0, 0], EndPoint = [3, 4, 0] } };
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("CreateWallSweptSolid",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object?[] arguments = method.GetParameters().Select(p => p.DefaultValue).ToArray();
        arguments[0] = writer; arguments[1] = wall; arguments[2] = new double[] { 0, -0.5, 0, 3.5, 4.5, 3 };
        arguments[3] = arguments[4] = arguments[5] = 1;
        method.Invoke(new IfcExporter(), arguments);
        var profile = Assert.Single(Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt),
            e => e.Type == "IFCRECTANGLEPROFILEDEF");
        Assert.EndsWith(",1.524,0.0762", profile.Args);
    }

    [Theory]
    [InlineData(null, 0.375)]
    [InlineData("raw_ids", 0.125)]
    [InlineData("sentinels", -2e30)]
    public void NonnullGridUsesOriginalEnvelopeNotLayersOrRegionTopology(string? mode, double expected)
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512,
            vertical: HostTypeVerticalRegionsTests.Vertical(mode), layers: [Layer(999)]);
        Assert.True(Read(block, 512, out double width));
        Assert.Equal(expected, width);
    }

    [Theory]
    [InlineData(2e30, 0, 1e30)] [InlineData(-2e30, 0, 1e30)]
    [InlineData(4, -1, -2e30)] [InlineData(4, 99, -2e30)]
    public void GridPreservesNativeEnvelopeSentinelsAndNonzeroOrientation(double coordinate, int orientation, double expected)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0u); writer.Write(0u); writer.Write(0u); // extension ids / map
        writer.Write(1u); writer.Write(coordinate); writer.Write(-5); writer.Write(orientation);
        writer.Write(-1); writer.Write(-1); // native region ids do not select segments
        writer.Write(0u); writer.Write(0d); // regions / tolerance
        writer.Write(0d); writer.Write(0d); writer.Write((byte)0);
        writer.Write(0u); writer.Write(0u); // sweeps / reveals
        byte[] block = HostTypeLayerSetNameTests.Block(512, vertical: stream.ToArray());
        Assert.True(Read(block, 512, out double width));
        Assert.Equal(expected, width);
    }

    [Theory]
    [InlineData("coordinate_nan")] [InlineData("coordinate_infinity")]
    [InlineData("later_cell")] [InlineData("preview")] [InlineData("segment_count")]
    public void GridRequiresCompleteValidatedOwnGraph(string mode)
        => Assert.False(Read(HostTypeLayerSetNameTests.Block(512,
            mode == "later_cell" ? "invalid_later_vertical_cell" : null,
            vertical: HostTypeVerticalRegionsTests.Vertical(mode)), 512, out _));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EveryDuplicateOrUnsupportedHostClearsGetterInEitherOrder(bool reverse)
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512);
        var state = new ParsingState();
        foreach (ushort cls in reverse ? new ushort[] { 105, 512 } : new ushort[] { 512, 105 })
            RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0,
                BitConverter.ToUInt32(block, 8), cls, 500);
        Assert.Null(state.BasicWallTypeThicknessFeet[500]);
        Assert.Equal(2, state.HostTypeCompoundStructures[500].BlockCount);
    }

    [Theory]
    [InlineData(null)] [InlineData("no_layers")] [InlineData("duplicate_host")]
    [InlineData("duplicate_type")] [InlineData("wrong_type")] [InlineData("no_value")]
    [InlineData("nan")] [InlineData("zero")] [InlineData("negative")]
    [InlineData("duplicate_element")] [InlineData("block_only")]
    [InlineData("missing_link")] [InlineData("wrong_category")]
    public void ExactOccurrenceTypeBindingIsPrivateAndFailClosed(string? mode)
    {
        var state = new ParsingState();
        byte[] block = HostTypeLayerSetNameTests.Block(512);
        RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0, BitConverter.ToUInt32(block, 8), 512, 500);
        state.SystemTypeRoots[500] = (1, 512, null);
        state.SystemOccurrenceTypeIds[42] = (1, 3524, 500);
        var type = new Element { Id = 500, CategoryId = -2000011 };
        var wall = new Element { Id = 42, CategoryId = -2000011,
            Layers = mode == "no_layers" ? null : [new MaterialLayer { ThicknessMm = 1 }],
            Parameters = [new Parameter { Id = -1001000, Value = 9d, StorageType = StorageType.Double }] };
        Element[] elements = mode == "duplicate_element" ? [wall, type, new Element { Id = 500 }] : [wall, type];
        switch (mode)
        {
            case "duplicate_host": state.HostTypeCompoundStructures[500] = (2, 512, null, null); break;
            case "duplicate_type": state.SystemTypeRoots[500] = (2, 512, null); break;
            case "wrong_type": state.SystemTypeRoots[500] = (1, 2559, null); break;
            case "no_value": state.BasicWallTypeThicknessFeet.Clear(); break;
            case "nan": state.BasicWallTypeThicknessFeet[500] = double.NaN; break;
            case "zero": state.BasicWallTypeThicknessFeet[500] = 0; break;
            case "negative": state.BasicWallTypeThicknessFeet[500] = -1; break;
            case "block_only": wall.IsBlockOnly = true; break;
            case "missing_link": state.SystemOccurrenceTypeIds.Clear(); break;
            case "wrong_category": wall.CategoryId = -2000038; break;
        }
        ElementBuilder.ApplySerializedSystemTypeIdentity(elements, state);
        string before = System.Text.Json.JsonSerializer.Serialize(elements);
        bool valid = mode is null or "no_layers";
        Assert.Equal(valid ? 1 : 0, ElementBuilder.ApplyBasicWallTypeThickness(elements, state));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(elements));
        if (valid)
        {
            Assert.Equal(0.375, wall.ValidatedWallTypeThicknessFeet);
            Assert.True(IfcExporter.TryGetSerializedWallWidthFeet(wall, out double width));
            Assert.Equal(0.375, width);
        }
        else Assert.Null(wall.ValidatedWallTypeThicknessFeet);
        Assert.Equal(0, ElementBuilder.ApplyBasicWallTypeThickness(elements, new ParsingState()));
        Assert.Null(wall.ValidatedWallTypeThicknessFeet);
    }
}
