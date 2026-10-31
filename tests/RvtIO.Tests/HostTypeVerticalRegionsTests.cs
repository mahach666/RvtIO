using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class HostTypeVerticalRegionsTests
{
    [Theory]
    [InlineData(512)] [InlineData(1915)] [InlineData(646)]
    public void OwnPointerChainPreservesOrderedRawGridFields(int cls)
    {
        byte[] block = HostTypeLayerSetNameTests.Block((ushort)cls,
            currentRefs: [(3, 29, 0)], vertical: Vertical());
        Assert.True(Extract(block, (ushort)cls, out var value));
        Assert.Equal(new[] { 40, 30 }, value.ExtendableRegionIdsTop);
        Assert.Equal(new[] { 30 }, value.ExtendableRegionIdsBottom);
        Assert.Equal(new[] { new RawRegionLayerPair(40, 1), new(30, 0) }, value.RegionToLayerMap);
        Assert.Equal(new RawGridSegment(-0.25, 3, 0, 30, -1), value.Segments[0]);
        Assert.Equal(new RawGridSegment(0, 4, 0, 30, 40), value.Segments[1]);
        Assert.Equal(new[] { 15, 3, 14, 4 }, value.Regions[0].SegmentIds);
        Assert.Equal(1e-9, value.Tolerance);
        Assert.Equal(3, value.SampleHeight);
        Assert.Equal(2.5, value.CutOffHeight);
        Assert.Equal(1, value.PreviewDimensionsValid);
    }

    [Fact]
    public void RawIdsAndOrientationsAreNotConvertedIntoPointersOrApiIndices()
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512, vertical: Vertical("raw_ids"));
        Assert.True(Extract(block, 512, out var value));
        Assert.Equal(new RawRegionLayerPair(-1, int.MinValue), value.RegionToLayerMap[0]);
        Assert.Equal(-1, value.Segments[0].Id);
        Assert.Equal(int.MaxValue, value.Segments[0].Orientation);
    }

    [Theory]
    [InlineData("top_count")] [InlineData("bottom_count")] [InlineData("map_count")]
    [InlineData("segment_count")] [InlineData("region_count")] [InlineData("nested_segment_count")]
    [InlineData("coordinate_nan")] [InlineData("coordinate_infinity")]
    [InlineData("tolerance_nan")] [InlineData("tolerance_negative")]
    [InlineData("height_nan")] [InlineData("cutoff_infinity")] [InlineData("preview")]
    public void InvalidCountsAndNonfiniteNumericFieldsFailClosed(string mode)
        => Assert.False(Extract(HostTypeLayerSetNameTests.Block(512, vertical: Vertical(mode)), 512, out _));

    [Fact]
    public void EveryTruncatedVerticalPayloadFailsClosed()
    {
        byte[] vertical = Vertical();
        for (int length = 0; length < vertical.Length; length++)
        {
            byte[] block = HostTypeLayerSetNameTests.Block(512, vertical: vertical[..length]);
            Assert.False(Extract(block, 512, out _));
        }
    }

    [Theory]
    [InlineData("wrong_vertical_class")] [InlineData("missing_pointer")]
    [InlineData("negative_owner")] [InlineData("tail")]
    public void InvalidOwnershipPointerOrFullGraphBoundaryIsRejected(string mode)
        => Assert.False(Extract(HostTypeLayerSetNameTests.Block(512, mode, vertical: Vertical()), 512, out _));

    [Fact]
    public void LaterUnreadableDeferredObjectCannotLeavePartialGridSource()
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512, "invalid_later_vertical_cell",
            vertical: Vertical("later_cell"));
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), 512, out _));
        Assert.False(Extract(block, 512, out var regions));
        Assert.Null(regions);
    }

    [Fact]
    public void EmptySentinelsPreserveTheirNativeEmptyArrayMeaning()
    {
        byte[] block = HostTypeLayerSetNameTests.Block(512, vertical: Vertical("sentinels"));
        Assert.True(Extract(block, 512, out var value));
        Assert.Empty(value.ExtendableRegionIdsTop);
        Assert.Empty(value.ExtendableRegionIdsBottom);
        Assert.Empty(value.RegionToLayerMap);
        Assert.Empty(value.Segments);
        Assert.Empty(value.Regions);
    }

    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);
    private static bool Extract(byte[] block, ushort cls, out RawHostVerticalRegions value)
        => TfBlockDeserializer.TryExtractHostTypeVerticalRegions(block, 0, Size(block), cls, out value);

    internal static byte[] Vertical(string? mode = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        bool sentinel = mode == "sentinels";
        Count(2, "top_count"); if (!sentinel) Write(40, 30);
        Count(1, "bottom_count"); if (!sentinel) Write(30);
        Count(2, "map_count");
        if (!sentinel) Write(mode == "raw_ids" ? -1 : 40,
            mode == "raw_ids" ? int.MinValue : 1, 30, 0);
        Count(7, "segment_count");
        if (!sentinel)
        {
            Segment(mode == "coordinate_nan" ? double.NaN : mode == "coordinate_infinity" ? double.PositiveInfinity : -0.25,
                mode == "raw_ids" ? -1 : 3, mode == "raw_ids" ? int.MaxValue : 0, 30, -1);
            Segment(0, 4, 0, 30, 40); Segment(0.125, 16, 0, 40, -1);
            Segment(0, 14, 1, 30, -1); Segment(3, 15, 1, 30, -1);
            Segment(0, 17, 1, 40, -1); Segment(3, 18, 1, 40, -1);
        }
        Count(2, "region_count");
        if (!sentinel)
        {
            writer.Write(30); Count(4, "nested_segment_count"); Write(15, 3, 14, 4);
            writer.Write(40); writer.Write(4u); Write(17, 4, 18, 16);
        }
        writer.Write(mode == "tolerance_nan" ? double.NaN : mode == "tolerance_negative" ? -1 : 1e-9);
        writer.Write(mode == "height_nan" ? double.NaN : 3d);
        writer.Write(mode == "cutoff_infinity" ? double.NegativeInfinity : 2.5);
        writer.Write(mode == "preview" ? (byte)2 : (byte)1);
        writer.Write(mode == "later_cell" ? 1u : 0u);
        if (mode == "later_cell") { writer.Write(uint.MaxValue); writer.Write((ushort)496); }
        writer.Write(0u);
        return stream.ToArray();

        void Count(uint count, string name) => writer.Write(mode == name ? 100001u : sentinel ? uint.MaxValue : count);
        void Write(params int[] values) { foreach (int value in values) writer.Write(value); }
        void Segment(double coordinate, int id, int orientation, int a, int b)
        { writer.Write(coordinate); Write(id, orientation, a, b); }
    }
}
