using System.Reflection;
using System.Text.Json;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallGeometrySixPhaseTests
{
    [Theory]
    [InlineData(4147, int.MinValue)] [InlineData(4147, 0)] [InlineData(4147, int.MaxValue)]
    [InlineData(677, int.MinValue)] [InlineData(677, 0)] [InlineData(677, int.MaxValue)]
    [InlineData(590, int.MinValue)] [InlineData(590, 0)] [InlineData(590, int.MaxValue)]
    public void AllSixPhasesReadLastOwnFieldsWithoutSelectingFromOpaqueVersion(int cls, int version)
    {
        var (bytes, end) = Block((ushort)cls, EmptyTail(cls), version, firstBase: true, reverse: true);
        var old = Legacy(bytes)!;
        Assert.True(old.Complete); Assert.Single(old.Steps); Assert.Empty(old.SimpleOwnTails);
        var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(value.Histories.Complete);
        Assert.Equal(2, value.Histories.Steps.Count); Assert.Equal(version, value.Histories.Steps[1].Version);
        Assert.Equal(3, value.Histories.TargetQueueIndex);
        Assert.Equal(end + EmptyTail(cls).Length, value.Histories.End);
        Assert.Equal(-17, Assert.Single(value.Histories.SimpleOwnTails).GeneratedWallCrossSection);
        Assert.Single(value.Histories.Steps[1].Tables[3].Entries);
        Assert.Equal(new[] { 9, -1, int.MinValue, int.MaxValue }, value.Histories.Steps[1].Tables[3].Entries[0].Keys);
        if (cls == 590) Assert.Single(value.GridOwnTails); else Assert.Single(value.TweakOwnTails);
    }

    [Theory]
    [InlineData(4147)] [InlineData(677)]
    public void InlineAffectedPairRetainsSignedValuesDuplicateOrderAndEverySpan(int cls)
    {
        byte[] tail = Tweak(cls, w =>
        {
            w.Write(3u);
            foreach (int id in new[] { -1, int.MinValue, -1 })
            { w.Write(id); w.Write(3u); w.Write(-1); w.Write(int.MaxValue); w.Write(-1); }
        });
        var (bytes, end) = Block((ushort)cls, tail);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        var own = Assert.Single(value.TweakOwnTails); Assert.Equal(2, own.QueueIndex);
        Assert.Equal(end, own.Start); Assert.Equal(end + tail.Length, own.End);
        Assert.Equal(new[] { -1, int.MinValue, -1 }, own.AffectedEdgesTags.Entries.Select(e => e.ElementId));
        Assert.All(own.AffectedEdgesTags.Entries, e =>
        {
            Assert.Equal(new[] { -1, int.MaxValue, -1 }, e.Tags.Values);
            Assert.Equal(e.Start + 4, e.Tags.Start); Assert.Equal(20, e.End - e.Start);
            Assert.Equal(e.End, e.Tags.End); Assert.Equal(3u, e.Tags.Count);
        });
        Assert.Empty(value.Histories.PriorObjects); // no false polymorphic wrapper pointer
        if (cls == 677)
        {
            Assert.Equal(int.MinValue, own.Pair!.OtherId);
            Assert.Equal((byte)255, own.Pair.UserSet); Assert.Equal((byte)2, own.Pair.AllowUserUnjoin);
            Assert.Equal((byte)0, own.Pair.IsAssociatedWithColJoinStep);
            Assert.Equal((byte)255, own.HasCoincFaces); Assert.Equal((byte)2, own.DoNothing);
            Assert.Null(own.IdGeomJoined);
        }
        else
        {
            Assert.Null(own.Pair); Assert.Null(own.HasCoincFaces); Assert.Null(own.DoNothing);
            Assert.Equal(new[] { -1, -1 }, own.IdGeomJoined!.Values);
        }
        Assert.Equal(new[] { int.MaxValue, int.MinValue }, own.HealingParents.Values);
    }

    [Theory]
    [InlineData(4147)] [InlineData(677)]
    public void NullNestedTagSetIsNotAnEmptyCollectionOrAPointer(int cls)
    {
        byte[] tail = Tweak(cls, w => { w.Write(2u); w.Write(7); w.Write(uint.MaxValue); w.Write(7); w.Write(0u); });
        var (bytes, _) = Block((ushort)cls, tail);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        var entries = Assert.Single(value.TweakOwnTails).AffectedEdgesTags.Entries;
        Assert.Equal(uint.MaxValue, entries[0].Tags.Count); Assert.Equal(0u, entries[1].Tags.Count);
        Assert.All(entries, e => { Assert.Empty(e.Tags.Values); Assert.Equal(8, e.End - e.Start); });
    }

    [Theory]
    [InlineData(4147)] [InlineData(677)] [InlineData(590)]
    public void NullOuterCountsRemainDistinctFromEmpty(int cls)
    {
        var tail = EmptyTail(cls);
        int offset = cls == 677 ? 7 : cls == 590 ? 4 : 0;
        BitConverter.GetBytes(uint.MaxValue).CopyTo(tail, offset);
        var (bytes, _) = Block((ushort)cls, tail);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        uint count = cls == 590 ? Assert.Single(value.GridOwnTails).Cells.Count
            : Assert.Single(value.TweakOwnTails).AffectedEdgesTags.Count;
        Assert.Equal(uint.MaxValue, count);
    }

    [Fact]
    public void GridPairsContainInlineCellsAndTwentyByteSegmentsNotPointersOrNativePadding()
    {
        var (bytes, end) = Block(590, Grid()); var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(value.Histories.Complete);
        var grid = Assert.Single(value.GridOwnTails); Assert.Equal(int.MinValue, grid.DriverId);
        var cell = Assert.Single(grid.Cells.Entries);
        Assert.Equal(-1, cell.U); Assert.Equal(int.MaxValue, cell.V);
        Assert.Equal(new[] { -1, -1, 7 }, cell.EdgeTags.Values);
        Assert.Equal(int.MinValue, cell.FaceTag); Assert.Equal(-1, cell.UGridline);
        Assert.Equal(0, cell.UGridline2); Assert.Equal(2, cell.VGridline);
        Assert.Equal(3, cell.VGridline2); Assert.Equal(4, cell.PanelId);
        Assert.Equal(48, cell.End - cell.Start);
        Assert.Equal(new[] { -1, -1 }, grid.EdgeMap.Entries.Select(e => e.First));
        Assert.Equal(new[] { int.MinValue, int.MaxValue }, grid.EdgeMap.Entries.Select(e => e.Second));
        var original = Assert.Single(grid.EdgeSegments.Entries); Assert.Equal(-1, original.OriginalEdge);
        Assert.Equal(2u, original.SegmentCount); Assert.All(original.Segments, s => Assert.Equal(20, s.End - s.Start));
        Assert.Equal(long.MinValue, original.Segments[0].StartParameterBits); // negative zero
        Assert.Equal(0x7ff8000000000042L, original.Segments[0].EndParameterBits); // NaN payload
        Assert.Equal(0x7ff0000000000000L, original.Segments[1].StartParameterBits); // infinity
        Assert.Equal(unchecked((long)0xfff0000000000000UL), original.Segments[1].EndParameterBits);
        Assert.Equal(new[] { -1, int.MinValue }, original.Segments.Select(e => e.EdgeTag));
        Assert.Equal(end, grid.Start); Assert.Equal(grid.End, value.Histories.End);
        Assert.Contains("9221120237041090626", JsonSerializer.Serialize(grid));
    }

    [Theory]
    [InlineData(4147, 0)] [InlineData(4147, 3)] [InlineData(4147, 4)] [InlineData(4147, 11)]
    [InlineData(677, 0)] [InlineData(677, 4)] [InlineData(677, 7)] [InlineData(677, 16)]
    [InlineData(590, 0)] [InlineData(590, 4)] [InlineData(590, 8)] [InlineData(590, 15)]
    public void TruncatedLastOwnFieldsAreAtomicAndDoNotMasqueradeAsCompletePrefix(int cls, int retained)
    {
        var tail = EmptyTail(cls); var (bytes, end) = Block((ushort)cls, tail[..retained]);
        AssertIncomplete(bytes, end, cls);
    }

    [Theory]
    [InlineData(4147, 100001u)] [InlineData(677, uint.MaxValue - 1)] [InlineData(590, 1u)]
    public void InvalidOuterCountsRetainCompletedBaseButNoPartialOwnFields(int cls, uint count)
    {
        byte[] tail = EmptyTail(cls);
        BitConverter.GetBytes(count).CopyTo(tail, cls == 677 ? 7 : cls == 590 ? 4 : 0);
        var (bytes, end) = Block((ushort)cls, tail); AssertIncomplete(bytes, end, cls);
    }

    [Theory]
    [InlineData(100001u)] [InlineData(uint.MaxValue - 1)] [InlineData(1u)]
    public void InvalidInnerCountCannotConsumeALaterOwnFieldAsATag(uint count)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(1u); w.Write(-1); w.Write(count);
        var (bytes, end) = Block(4147, stream.ToArray()); AssertIncomplete(bytes, end, 4147);
    }

    [Fact]
    public void OneHundredThousandTagsAreAcceptedAtTheBoundedLimit()
    {
        var tail = Tweak(4147, w =>
        { w.Write(1u); w.Write(-1); w.Write(100000u); for (int i = 0; i < 100000; i++) w.Write(i); });
        var (bytes, _) = Block(4147, tail); var value = Read(bytes, out var failure)!; Assert.Null(failure);
        var tags = Assert.Single(Assert.Single(value.TweakOwnTails).AffectedEdgesTags.Entries).Tags;
        Assert.Equal(100000u, tags.Count); Assert.Equal(99999, tags.Values[^1]);
    }

    [Theory]
    [InlineData(4)] [InlineData(8)] [InlineData(12)]
    public void EveryGridOuterCollectionChecksCountAndAvailableMinimumRowBytes(int offset)
    {
        var tail = EmptyTail(590); BitConverter.GetBytes(100001u).CopyTo(tail, offset);
        var (bytes, end) = Block(590, tail); AssertIncomplete(bytes, end, 590);
    }

    [Fact]
    public void IncompleteSecondSegmentDoesNotPublishPreviouslyCompletedCellsAndEdges()
    {
        var (bytes, end) = Block(590, Grid()[..^1]); AssertIncomplete(bytes, end, 590);
    }

    [Fact]
    public void NestedGridSentinelsDoNotEnqueueOrFabricateMissingMembers()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(-1); w.Write(1u); w.Write(-1); w.Write(-1); w.Write(uint.MaxValue);
        w.Write(new byte[24]); w.Write(0u); w.Write(1u); w.Write(-1); w.Write(uint.MaxValue);
        var (bytes, _) = Block(590, stream.ToArray()); var value = Read(bytes, out var failure)!;
        Assert.Null(failure); var grid = Assert.Single(value.GridOwnTails);
        Assert.Equal(uint.MaxValue, Assert.Single(grid.Cells.Entries).EdgeTags.Count);
        var original = Assert.Single(grid.EdgeSegments.Entries);
        Assert.Equal(uint.MaxValue, original.SegmentCount); Assert.Empty(original.Segments);
        Assert.Equal(4, original.SegmentsEnd - original.SegmentsStart);
    }

    [Fact]
    public void LastBaseOnlyClassNeedsNoOwnBytesAndLaterBodiesRemainOutsideScope()
    {
        var (bytes, end) = Block(365, []);
        var value = Read(bytes, out var failure)!; Assert.Null(failure); Assert.True(value.Histories.Complete);
        Assert.Equal(end, value.Histories.End); Assert.Empty(value.TweakOwnTails); Assert.Empty(value.GridOwnTails);
    }

    [Theory]
    [InlineData(int.MinValue)] [InlineData(0)] [InlineData(5)] [InlineData(int.MaxValue)]
    public void CutoutSketchOwnIdentitiesAndRawBytesDoNotDependOnOpaqueStepVersion(int version)
    {
        byte[] tail = BitConverter.GetBytes(int.MinValue).Concat(BitConverter.GetBytes(-1)).Concat(new byte[] { 255, 2 }).ToArray();
        var (bytes, end) = Block(982, tail, version, reverse: true);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        var own = Assert.Single(value.CutoutSketchOwnTails);
        Assert.Equal(end, own.Start); Assert.Equal(end + 10, own.End);
        Assert.Equal(int.MinValue, own.SketchId); Assert.Equal(-1, own.UserId);
        Assert.Equal((byte)255, own.IsTrueBoolean); Assert.Equal((byte)2, own.IsProjectOpening);
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(8)] [InlineData(9)]
    public void CutoutOwnTruncationKeepsTheLastBaseBoundary(int retained)
    {
        var (bytes, end) = Block(982, new byte[retained]);
        AssertIncomplete(bytes, end, 982);
        Assert.Empty(Read(bytes, out _)!.CutoutSketchOwnTails);
    }

    [Theory]
    [InlineData("null")] [InlineData("external")] [InlineData("handle")] [InlineData("wrong-class")]
    public void SixthPhaseUnconfirmedPointerIsRetainedAndExplicitlyRejected(string mutation)
    {
        var (bytes, _) = Block(4147, EmptyTail(4147), firstBase: true, pointer: mutation);
        Assert.True(Legacy(bytes)!.Complete);
        var value = Read(bytes, out var failure)!;
        Assert.False(value.Histories.Complete); Assert.StartsWith("phase-pointer:", failure);
        Assert.Single(value.Histories.List.Phases[5].Members); Assert.Empty(value.TweakOwnTails);
    }

    [Theory]
    [InlineData(4147)] [InlineData(677)] [InlineData(590)]
    public void NonzeroFrameStartAndArcWallKeepAllNestedSpansFrameRelative(int cls)
    {
        var (bytes, _) = Block((ushort)cls, cls == 590 ? Grid() : Tweak(cls, w => w.Write(0u)), root: 361);
        var embedded = new byte[bytes.Length + 19]; bytes.CopyTo(embedded, 7);
        var value = TfBlockDeserializer.ExtractOwnWallSixPhaseGeometryHistories(embedded, 7,
            BitConverter.ToUInt32(bytes, 8), 361, out var failure);
        Assert.Null(failure); Assert.Equal(JsonSerializer.Serialize(Read(bytes, out _)), JsonSerializer.Serialize(value));
    }

    [Theory]
    [InlineData("owner")] [InlineData("footer")] [InlineData("null-document")]
    public void InvalidOwnRootDoesNotProduceSixPhaseValidation(string mutation)
    {
        var (bytes, _) = WallGeometryPhaseTests.Block(3524, mutation);
        Assert.Null(Read(bytes, out var failure)); Assert.NotNull(failure);
    }

    [Theory]
    [InlineData(365)] [InlineData(590)] [InlineData(977)] [InlineData(4098)]
    public void TweakSchemaGuardDoesNotAdmitOtherLayouts(int cls)
    {
        var guard = typeof(TfBlockDeserializer).GetMethod("HasWallTweakOwnTailSchema", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)guard.Invoke(null, [(ushort)cls])!);
    }

    private static void AssertIncomplete(byte[] bytes, int end, int cls)
    {
        var result = Read(bytes, out var failure)!;
        Assert.False(result.Histories.Complete); Assert.Equal($"step-tail:2:{cls}", failure);
        Assert.Equal(end, result.Histories.End); Assert.Single(result.Histories.Steps);
        Assert.Empty(result.TweakOwnTails); Assert.Empty(result.GridOwnTails);
    }

    private static TfBlockDeserializer.OwnWallSixPhaseGeometryHistories? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallSixPhaseGeometryHistories(bytes, 0,
            BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt16(bytes, 12), out failure);
    private static TfBlockDeserializer.OwnWallGeometryHistories? Legacy(byte[] bytes)
        => TfBlockDeserializer.ExtractOwnWallGeometryHistories(bytes, 0,
            BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt16(bytes, 12), out _);

    private static byte[] EmptyTail(int cls)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        if (cls == 677) { w.Write(-1); w.Write(new byte[3]); }
        if (cls == 590) w.Write(-1);
        w.Write(new byte[cls == 677 ? 8 : 12]);
        if (cls == 677) w.Write(new byte[2]);
        return stream.ToArray();
    }

    private static byte[] Tweak(int cls, Action<BinaryWriter> map)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        if (cls == 677) { w.Write(int.MinValue); w.Write(new byte[] { 255, 2, 0 }); }
        map(w);
        if (cls == 4147) { w.Write(2u); w.Write(-1); w.Write(-1); }
        w.Write(2u); w.Write(int.MaxValue); w.Write(int.MinValue);
        if (cls == 677) w.Write(new byte[] { 255, 2 });
        return stream.ToArray();
    }

    private static byte[] Grid()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(int.MinValue); w.Write(1u); w.Write(-1); w.Write(int.MaxValue);
        w.Write(3u); w.Write(-1); w.Write(-1); w.Write(7);
        foreach (int n in new[] { int.MinValue, -1, 0, 2, 3, 4 }) w.Write(n);
        w.Write(2u); w.Write(-1); w.Write(int.MinValue); w.Write(-1); w.Write(int.MaxValue);
        w.Write(1u); w.Write(-1); w.Write(2u);
        w.Write(long.MinValue); w.Write(0x7ff8000000000042L); w.Write(-1);
        w.Write(0x7ff0000000000000L); w.Write(unchecked((long)0xfff0000000000000UL)); w.Write(int.MinValue);
        return stream.ToArray();
    }

    internal static (byte[] Bytes, int PrefixEnd) Block(ushort cls, byte[] tail, int version = 0,
        bool firstBase = false, bool reverse = false, ushort root = 3524, string? pointer = null)
    {
        var (original, offsets) = WallGeometryPhaseTests.Block(root);
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(original.AsSpan(0, offsets["list"]));
        for (int phase = 0; phase < 6; phase++)
        {
            bool first = firstBase && phase == 0;
            w.Write(first || phase == 5 ? 1u : 0u);
            if (first) { w.Write(uint.MaxValue); w.Write((ushort)500); }
            if (phase != 5) continue;
            w.Write(pointer == "null" ? 0u : pointer == "handle" ? 7u : uint.MaxValue);
            if (pointer != "null") w.Write(pointer == "external" ? (ushort)0 : pointer == "wrong-class" ? (ushort)3905 : cls);
        }
        w.Write(original.AsSpan(offsets["list"] + 24, offsets["list-end"] - offsets["list"] - 24));
        if (firstBase) { Prefix(-87, false); w.Write(-17); }
        Prefix(version, reverse); int end = (int)stream.Position; w.Write(tail);
        w.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return (bytes, end);

        void Prefix(int v, bool rows)
        {
            w.Write(new byte[12]); w.Write(rows ? 1u : 0u);
            if (rows) foreach (int n in new[] { -1, 9, -1, int.MinValue, int.MaxValue }) w.Write(n);
            w.Write(17); w.Write(v); w.Write(int.MinValue); w.Write(2u);
        }
    }
}
