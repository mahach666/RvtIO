using Xunit;

namespace RvtIO.Tests;

public class RootProbeStairLayoutTests
{
    [Fact]
    public void LayoutRetainsEverySectionAndDeferredPointerInFieldOrder()
    {
        byte[] bytes = Layout();
        Assert.True(StairBoundaryProbe.TryLayoutInfo(bytes, 3, bytes.Length - 4, out int end, out var layout, out var queue));
        Assert.Equal(bytes.Length - 4, end);
        Assert.NotNull(layout);
        Assert.Equal(new StairBoundaryProbe.Entry(-0.25, -1, int.MinValue), Assert.Single(Assert.Single(layout.Intersections)));
        Assert.Equal(new StairBoundaryProbe.CurveData(7, -1, 2, 3), Assert.Single(Assert.Single(layout.Boundaries).Records));
        Assert.Equal(new[] { new StairBoundaryProbe.Pointer(5, 1736), new(0, null), new(6, 0) }, layout.Boundaries[0].Curves);
        Assert.Empty(layout.Risers);
        Assert.Equal(new[] { 2, 0, 1 }, layout.SortedRiserIndices);
        Assert.Equal(new StairBoundaryProbe.OrderedRiser(0.5, -0.5, 1, 2, -1, 4, 5, 6,
            new(8, 1974), new(9, 0)), Assert.Single(layout.OrderedRisers));
        Assert.Equal((byte)1, layout.LeadingIsLeft);
        Assert.Equal(new[] { ((ushort)1736, 5u), ((ushort)1974, 8u) }, queue);
    }

    [Fact]
    public void EveryLayoutTruncationRejectsWithoutPartialValueOrQueue()
    {
        byte[] bytes = Layout();
        for (int limit = 0; limit < bytes.Length - 4; limit++)
            RejectLayout(bytes, 3, limit);
    }

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void EmptyLayoutStillIncludesAllFiveCountsAndTheBoolean(uint count)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        for (int i = 0; i < 5; i++) writer.Write(count);
        writer.Write((byte)0); writer.Write(123u);
        byte[] bytes = stream.ToArray();
        Assert.True(StairBoundaryProbe.TryLayoutInfo(bytes, 0, bytes.Length, out int end, out var layout, out var queue));
        Assert.Equal(21, end); Assert.NotNull(layout); Assert.Empty(queue);
        Assert.Empty(layout.Intersections); Assert.Empty(layout.Boundaries); Assert.Empty(layout.Risers);
        Assert.Empty(layout.SortedRiserIndices); Assert.Empty(layout.OrderedRisers);
    }

    [Theory]
    [InlineData("outer")] [InlineData("inner")] [InlineData("boundary")] [InlineData("records")]
    [InlineData("pointers")] [InlineData("sorted")] [InlineData("ordered")]
    [InlineData("unknown_tag")] [InlineData("inline_ginfo_tag")] [InlineData("nan_intersection")]
    [InlineData("nan_ordered")] [InlineData("infinite_angle")] [InlineData("boolean")]
    public void MalformedLayoutIsNeverResynchronized(string mutation)
    {
        byte[] bytes = Layout();
        int location = mutation switch
        {
            "outer" => 3, "inner" => 7, "boundary" => 27, "records" => 31,
            "pointers" => 51, "sorted" => 75, "ordered" => 91, _ => -1,
        };
        if (location >= 0) BitConverter.GetBytes(100001u).CopyTo(bytes, location);
        if (mutation is "unknown_tag" or "inline_ginfo_tag")
            BitConverter.GetBytes(mutation == "unknown_tag" ? ushort.MaxValue : (ushort)4).CopyTo(bytes, 59);
        if (mutation == "nan_intersection") BitConverter.GetBytes(double.NaN).CopyTo(bytes, 11);
        if (mutation == "nan_ordered") BitConverter.GetBytes(double.NaN).CopyTo(bytes, 95);
        if (mutation == "infinite_angle") BitConverter.GetBytes(double.PositiveInfinity).CopyTo(bytes, 111);
        if (mutation == "boolean") bytes[^5] = 2;
        RejectLayout(bytes, 3, bytes.Length - 4);
    }

    [Theory]
    [InlineData(-1, 158)] [InlineData(int.MaxValue, 158)]
    [InlineData(3, -1)] [InlineData(3, int.MinValue)] [InlineData(3, int.MaxValue)]
    public void LayoutBoundsAreCheckedBeforeReading(int start, int end) => RejectLayout(Layout(), start, end);

    [Fact]
    public void CurrentHistoryRetainsAZeroRecordIdAndAllFourTables()
    {
        byte[] bytes = Histories();
        Assert.True(StairBoundaryProbe.TryCurrentHistoryEnd(bytes, 0, bytes.Length, out int end));
        Assert.Equal(bytes.Length - 16, end);
        for (int limit = 0; limit < bytes.Length; limit++)
        {
            Assert.False(StairBoundaryProbe.TryCurrentHistoryEnd(bytes, 0, limit, out int incomplete));
            Assert.Equal(-1, incomplete);
        }
    }

    [Theory]
    [InlineData("face_count")] [InlineData("curve_count")] [InlineData("edge_count")] [InlineData("reverse_count")]
    [InlineData("negative_id")] [InlineData("duplicate_id")] [InlineData("owner")]
    public void CurrentHistoryRejectsBadCountsIdsAndOwner(string mutation)
    {
        byte[] bytes = Histories();
        int position = mutation switch { "face_count" => 0, "curve_count" => 36, "edge_count" => 88, "reverse_count" => 132, _ => -1 };
        if (position >= 0) BitConverter.GetBytes(100001u).CopyTo(bytes, position);
        if (mutation == "negative_id") BitConverter.GetBytes(-1).CopyTo(bytes, 4);
        if (mutation == "duplicate_id") BitConverter.GetBytes(0).CopyTo(bytes, 20);
        if (mutation == "owner") BitConverter.GetBytes(99).CopyTo(bytes, bytes.Length - 4);
        Assert.False(StairBoundaryProbe.TryCurrentHistoryEnd(bytes, 0, bytes.Length, out int end));
        Assert.Equal(-1, end);
    }

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void CurrentHistoryEmptyMarkersDoNotConsumeTheNamedTail(uint marker)
    {
        byte[] bytes = new byte[32];
        for (int pos = 0; pos < 16; pos += 4) BitConverter.GetBytes(marker).CopyTo(bytes, pos);
        BitConverter.GetBytes(2u).CopyTo(bytes, 28);
        Assert.True(StairBoundaryProbe.TryCurrentHistoryEnd(bytes, 0, bytes.Length, out int end));
        Assert.Equal(16, end);
    }

    [Fact]
    public void AffectedTagsUseNestedInlineWrapperAndRetainValuesAndOrder()
    {
        byte[] bytes = Tags();
        Assert.True(StairBoundaryProbe.TryAffectedTags(bytes, 0, bytes.Length, out int end, out var tags));
        Assert.Equal(bytes.Length - 4, end);
        Assert.Equal(new[] { 500, -1 }, tags.Select(x => x.ElementId));
        Assert.Equal(new[] { -1, 0, 4 }, tags[0].Tags); Assert.Empty(tags[1].Tags);
        for (int limit = 0; limit < bytes.Length - 4; limit++)
        {
            Assert.False(StairBoundaryProbe.TryAffectedTags(bytes, 0, limit, out int incomplete, out var partial));
            Assert.Equal(-1, incomplete); Assert.Empty(partial);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(8)]
    public void AffectedTagsRejectUnboundedOuterAndInnerCounts(int offset)
    {
        byte[] bytes = Tags(); BitConverter.GetBytes(100001u).CopyTo(bytes, offset);
        Assert.False(StairBoundaryProbe.TryAffectedTags(bytes, 0, bytes.Length, out int end, out var partial));
        Assert.Equal(-1, end); Assert.Empty(partial);
    }

    [Theory]
    [InlineData(-1, 200)] [InlineData(int.MaxValue, 200)]
    [InlineData(0, -1)] [InlineData(0, int.MinValue)] [InlineData(0, int.MaxValue)]
    public void HistoryAndTagBoundsReturnNoPartialOutput(int start, int end)
    {
        Assert.False(StairBoundaryProbe.TryCurrentHistoryEnd(Histories(), start, end, out int historyEnd));
        Assert.Equal(-1, historyEnd);
        Assert.False(StairBoundaryProbe.TryAffectedTags(Tags(), start, end, out int tagsEnd, out var partial));
        Assert.Equal(-1, tagsEnd); Assert.Empty(partial);
    }

    private static void RejectLayout(byte[] bytes, int start, int limit)
    {
        Assert.False(StairBoundaryProbe.TryLayoutInfo(bytes, start, limit, out int end, out var layout, out var queue));
        Assert.Equal(-1, end); Assert.Null(layout); Assert.Empty(queue);
    }

    private static byte[] Layout()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[3]);
        writer.Write(1u); writer.Write(1u); writer.Write(-0.25); writer.Write(-1); writer.Write(int.MinValue);
        writer.Write(1u); writer.Write(1u); writer.Write(7); writer.Write(-1); writer.Write(2); writer.Write(3);
        writer.Write(3u); Ptr(5, 1736); Ptr(0, 0); Ptr(6, 0);
        writer.Write(0u); writer.Write(3u); writer.Write(2); writer.Write(0); writer.Write(1);
        writer.Write(1u); writer.Write(0.5); writer.Write(-0.5); writer.Write(1.0); writer.Write(2.0);
        writer.Write(-1); writer.Write(4); writer.Write(5); writer.Write(6); Ptr(8, 1974); Ptr(9, 0);
        writer.Write((byte)1); writer.Write(123u);
        return stream.ToArray();
        void Ptr(uint handle, ushort cls) { writer.Write(handle); if (handle != 0) writer.Write(cls); }
    }

    private static byte[] Histories()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (int size in new[] { 16, 24, 20, 20 })
        {
            writer.Write(2u);
            for (int id = 0; id < 2; id++) { writer.Write(id); writer.Write(new byte[size - 4]); }
        }
        writer.Write(9); writer.Write(1); writer.Write(123); writer.Write(2u);
        return stream.ToArray();
    }

    private static byte[] Tags()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(2u); writer.Write(500); writer.Write(3u); writer.Write(-1); writer.Write(0); writer.Write(4);
        writer.Write(-1); writer.Write(uint.MaxValue); writer.Write(123u);
        return stream.ToArray();
    }
}
