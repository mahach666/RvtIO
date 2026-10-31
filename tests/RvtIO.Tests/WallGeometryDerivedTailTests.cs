using System.Reflection;
using System.Text.Json;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallGeometryDerivedTailTests
{
    [Theory]
    [InlineData(150, int.MinValue)] [InlineData(150, 1)] [InlineData(150, int.MaxValue)]
    [InlineData(4098, int.MinValue)] [InlineData(4098, 0)] [InlineData(4098, int.MaxValue)]
    [InlineData(4166, int.MinValue)] [InlineData(4166, 2)] [InlineData(4166, int.MaxValue)]
    [InlineData(4167, int.MinValue)] [InlineData(4167, 5)] [InlineData(4167, int.MaxValue)]
    public void SourceOwnLayoutsDoNotSelectFromOpaqueGeomStepVersion(int cls, int version)
    {
        var (bytes, end) = WallGeometrySimpleTailTests.Block((ushort)cls, version, Tail(cls));
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete); Assert.Equal(2, result.Steps.Count);
        Assert.Equal(3, result.Steps[1].QueueIndex); // inline IDs/pairs do not enqueue pointers
        Assert.Empty(result.SimpleOwnTails);
        int expectedEnd = end + (cls == 150 ? 5 : cls == 4098 ? 8 : cls == 4166 ? 4 : 12);
        Assert.Equal(expectedEnd, result.Steps[1].Start);
        if (cls == 150)
        {
            var tail = Assert.Single(result.MemberGeometryOwnTails);
            Assert.Equal(2, tail.QueueIndex); Assert.Equal(end, tail.Start); Assert.Equal(expectedEnd, tail.End);
            Assert.Equal(-1, tail.MemberId); Assert.Equal((byte)255, tail.DidCutSomething);
            Assert.Empty(result.LayerExtensionOwnTails); Assert.Empty(result.SweepIdentityOwnTails);
        }
        else if (cls == 4098)
        {
            var tail = Assert.Single(result.LayerExtensionOwnTails);
            Assert.Equal(2, tail.QueueIndex); Assert.Equal(end, tail.Start); Assert.Equal(expectedEnd, tail.End);
            Assert.Equal(new[] { "m_otherId0", "m_otherId1" }, tail.Sides.Select(s => s.Name));
            Assert.All(tail.Sides, s => { Assert.Equal(0u, s.Count); Assert.Empty(s.Entries); });
            Assert.Empty(result.SweepIdentityOwnTails);
        }
        else
        {
            var tail = Assert.Single(result.SweepIdentityOwnTails);
            Assert.Equal(2, tail.QueueIndex); Assert.Equal((ushort)cls, tail.ClassId);
            Assert.Equal(end, tail.Start); Assert.Equal(expectedEnd, tail.End); Assert.Equal(-1, tail.WallSweepId);
            Assert.Equal(cls == 4167 ? (int?)int.MinValue : null, tail.Side);
            Assert.Equal(cls == 4167 ? (int?)int.MaxValue : null, tail.FixedSweepInstanceTag);
            Assert.Empty(result.LayerExtensionOwnTails);
        }
    }

    [Theory]
    [InlineData(int.MinValue, (byte)0)] [InlineData(-1, (byte)1)]
    [InlineData(0, (byte)255)] [InlineData(int.MaxValue, (byte)2)]
    public void MemberIdAndCutByteRemainRawWithoutInventingGeometryOrAResolvedElement(int id, byte value)
    {
        byte[] tail = BitConverter.GetBytes(id).Concat(new[] { value }).ToArray();
        var (bytes, end) = WallGeometrySimpleTailTests.Block(150, 1, tail);
        var result = Read(bytes, out var failure)!; Assert.Null(failure); Assert.True(result.Complete);
        var member = Assert.Single(result.MemberGeometryOwnTails);
        Assert.Equal(id, member.MemberId); Assert.Equal(value, member.DidCutSomething);
        Assert.Equal(end + 5, member.End); Assert.Equal(3, result.Steps[1].QueueIndex);
    }

    [Fact]
    public void LayerCollectionsKeepEverySignedPairDuplicateOrderAndByteSpan()
    {
        byte[] tail = Layers([(-1, int.MinValue), (7, -1), (-1, int.MinValue)], [(int.MaxValue, 0)]);
        var (bytes, end) = WallGeometrySimpleTailTests.Block(4098, -87, tail, reverse: true);
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        var value = Assert.Single(result.LayerExtensionOwnTails);
        Assert.Equal(end, value.Start); Assert.Equal(end + 40, value.End);
        var first = value.Sides[0]; var second = value.Sides[1];
        Assert.Equal(3u, first.Count); Assert.Equal(1u, second.Count);
        Assert.Equal(new[] { -1, 7, -1 }, first.Entries.Select(p => p.First));
        Assert.Contains("\"First\":-1", JsonSerializer.Serialize(first));
        Assert.Equal(new[] { int.MinValue, -1, int.MinValue }, first.Entries.Select(p => p.ElementId));
        Assert.Equal(end + 4, first.Entries[0].Start); Assert.Equal(end + 28, first.End);
        Assert.All(first.Entries, p => Assert.Equal(8, p.End - p.Start));
        Assert.Equal(first.End, second.Start); Assert.Equal(int.MaxValue, Assert.Single(second.Entries).First);
        Assert.Equal(0, second.Entries[0].ElementId);
        Assert.Single(result.Steps[0].Tables[3].Entries); // no legacy empty-reverse admission
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void NullCollectionSentinelRemainsDifferentFromEmpty(int side)
    {
        byte[] tail = Layers([], []); BitConverter.GetBytes(uint.MaxValue).CopyTo(tail, side * 4);
        var (bytes, _) = WallGeometrySimpleTailTests.Block(4098, 0, tail);
        var result = Read(bytes, out var failure)!; Assert.Null(failure);
        var sides = Assert.Single(result.LayerExtensionOwnTails).Sides;
        Assert.Equal(uint.MaxValue, sides[side].Count); Assert.Equal(0u, sides[1-side].Count);
        Assert.All(sides, s => { Assert.Empty(s.Entries); Assert.Equal(4, s.End - s.Start); });
    }

    [Theory]
    [InlineData(0, 100001u)] [InlineData(1, 100001u)]
    [InlineData(0, uint.MaxValue-1)] [InlineData(1, uint.MaxValue-1)]
    [InlineData(0, 1u)] [InlineData(1, 1u)]
    public void UnboundedOrUnavailableLayerRowsDoNotPromotePartialOwnTail(int side, uint count)
    {
        byte[] tail = Layers([], []); BitConverter.GetBytes(count).CopyTo(tail, side * 4);
        var (bytes, end) = WallGeometrySimpleTailTests.Block(4098, 0, tail, appendNext: false);
        AssertIncomplete(bytes, end, 4098);
    }

    [Fact]
    public void CountLimitIncludesTheExactHundredThousandBoundary()
    {
        var pairs = Enumerable.Range(0, 100_000).Select(i => (i, -1)).ToArray();
        var (bytes, _) = WallGeometrySimpleTailTests.Block(4098, 0, Layers(pairs, []));
        var result = Read(bytes, out var failure)!; Assert.Null(failure); Assert.True(result.Complete);
        var side = Assert.Single(result.LayerExtensionOwnTails).Sides[0];
        Assert.Equal(100_000u, side.Count); Assert.Equal(100_000, side.Entries.Count);
        Assert.Equal(99_999, side.Entries[^1].First); Assert.Equal(-1, side.Entries[^1].ElementId);
    }

    [Theory]
    [InlineData(150, 0)] [InlineData(150, 3)] [InlineData(150, 4)]
    [InlineData(4098, 0)] [InlineData(4098, 3)] [InlineData(4098, 4)] [InlineData(4098, 7)]
    [InlineData(4166, 0)] [InlineData(4166, 3)]
    [InlineData(4167, 0)] [InlineData(4167, 4)] [InlineData(4167, 8)] [InlineData(4167, 11)]
    public void TruncationRetainsBasePrefixAndNoPartialOwnFields(int cls, int retained)
    {
        var (bytes, end) = WallGeometrySimpleTailTests.Block((ushort)cls, 0, Tail(cls)[..retained], appendNext: false);
        AssertIncomplete(bytes, end, cls);
    }

    [Fact]
    public void CompletedFirstLayerSideIsNotPublishedWhenSecondIsTruncated()
    {
        byte[] tail = Layers([(9, 17)], [(10, -1)]);
        var (bytes, end) = WallGeometrySimpleTailTests.Block(4098, 0, tail[..^1], appendNext: false);
        AssertIncomplete(bytes, end, 4098);
    }

    [Theory]
    [InlineData(150)] [InlineData(4098)] [InlineData(4166)] [InlineData(4167)]
    public void LastOwnTailIsOutsideRequestedPrefixEvenWithNoAvailableTailBytes(int cls)
    {
        var (bytes, end) = WallGeometrySimpleTailTests.Block((ushort)cls, int.MinValue, [], last: true);
        var result = Read(bytes, out var failure)!; Assert.Null(failure); Assert.True(result.Complete);
        Assert.Single(result.Steps); Assert.Equal(end, result.End);
        Assert.Empty(result.LayerExtensionOwnTails); Assert.Empty(result.SweepIdentityOwnTails);
        Assert.Empty(result.MemberGeometryOwnTails);
    }

    [Theory]
    [InlineData(150)] [InlineData(4098)] [InlineData(4166)] [InlineData(4167)]
    public void EmbeddedArcWallKeepsEveryNestedSpanFrameRelative(int cls)
    {
        var (bytes, _) = WallGeometrySimpleTailTests.Block((ushort)cls, 0, Tail(cls), root: 361);
        var embedded = new byte[bytes.Length + 19]; bytes.CopyTo(embedded, 7);
        var actual = TfBlockDeserializer.ExtractOwnWallGeometryHistories(embedded, 7,
            BitConverter.ToUInt32(bytes, 8), 361, out var failure);
        Assert.Null(failure); Assert.Equal(JsonSerializer.Serialize(Read(bytes, out _)), JsonSerializer.Serialize(actual));
    }

    [Theory]
    [InlineData(365)] [InlineData(500)] [InlineData(4165)] [InlineData(2262)]
    public void DerivedGuardDoesNotAdmitOtherTailClasses(int cls)
    {
        var method = typeof(TfBlockDeserializer).GetMethod("HasDerivedWallOwnTailSchema", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)method.Invoke(null, [(ushort)cls])!);
    }

    private static void AssertIncomplete(byte[] bytes, int end, int cls)
    {
        var result = Read(bytes, out var failure)!;
        Assert.False(result.Complete); Assert.Equal($"step-tail:2:{cls}", failure);
        Assert.Single(result.Steps); Assert.Equal(end, result.End);
        Assert.Empty(result.LayerExtensionOwnTails); Assert.Empty(result.SweepIdentityOwnTails);
        Assert.Empty(result.MemberGeometryOwnTails);
    }

    private static TfBlockDeserializer.OwnWallGeometryHistories? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallGeometryHistories(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out failure);

    private static byte[] Tail(int cls)
        => cls == 150 ? BitConverter.GetBytes(-1).Concat(new byte[] { 255 }).ToArray()
            : cls == 4098 ? Layers([], []) : cls == 4166 ? BitConverter.GetBytes(-1)
            : BitConverter.GetBytes(-1).Concat(BitConverter.GetBytes(int.MinValue)).Concat(BitConverter.GetBytes(int.MaxValue)).ToArray();

    private static byte[] Layers((int First, int ElementId)[] first, (int First, int ElementId)[] second)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var pairs in new[] { first, second })
        {
            writer.Write((uint)pairs.Length);
            foreach (var pair in pairs) { writer.Write(pair.First); writer.Write(pair.ElementId); }
        }
        return stream.ToArray();
    }
}
