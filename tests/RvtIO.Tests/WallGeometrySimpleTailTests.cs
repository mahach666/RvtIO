using System.Reflection;
using System.Text.Json;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallGeometrySimpleTailTests
{
    [Theory]
    [InlineData(int.MinValue)] [InlineData(-87)] [InlineData(0)] [InlineData(7)] [InlineData(int.MaxValue)]
    public void BaseWallOwnScalarLayoutDoesNotDependOnOpaqueStepVersion(int version)
    {
        var (bytes, end) = Block(500, version, BitConverter.GetBytes(int.MinValue));
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete); Assert.Equal(2, result.Steps.Count);
        var tail = Assert.Single(result.SimpleOwnTails);
        Assert.Equal(500, tail.ClassId); Assert.Equal(2, tail.QueueIndex);
        Assert.Equal(int.MinValue, tail.GeneratedWallCrossSection);
        Assert.Equal(end, tail.Start); Assert.Equal(end + 4, tail.End);
        Assert.Null(tail.Geometry); Assert.Null(tail.SideTags); Assert.Null(tail.Sides);
        Assert.Equal(tail.End, result.Steps[1].Start);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(17)]
    public void SweepRetainsNullGeometryAndFixedRawSideFieldsForOpaqueVersions(int version)
    {
        var (bytes, end) = Block(4165, version, Sweep(0, null));
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        var tail = Assert.Single(result.SimpleOwnTails);
        Assert.Equal(end + 14, tail.End); Assert.Null(tail.GeneratedWallCrossSection);
        Assert.Equal(new[] { -1, int.MinValue }, tail.SideTags);
        Assert.Equal(new byte[] { 0, 255 }, tail.Sides); // raw persisted bytes, not guessed booleans
        Assert.Equal(0u, tail.Geometry!.Handle); Assert.Null(tail.Geometry.ClassId);
        Assert.Null(tail.Geometry.QueueIndex); Assert.Equal(4, tail.Geometry.End - tail.Geometry.Start);
        Assert.Contains("\"Sides\":[0,255]", JsonSerializer.Serialize(tail));
    }

    [Theory]
    [InlineData(uint.MaxValue, 1736, 4)]
    [InlineData(17u, 1736, 4)]
    [InlineData(uint.MaxValue, 0, -1)]
    public void TaggedGeometryRetainsHandleClassAndExactDeferredQueuePosition(uint handle, int cls, int index)
    {
        var (bytes, _) = Block(4165, 0, Sweep(handle, (ushort)cls));
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        var pointer = Assert.Single(result.SimpleOwnTails).Geometry!;
        Assert.Equal(handle, pointer.Handle); Assert.Equal((ushort)cls, pointer.ClassId);
        Assert.Equal(index < 0 ? (int?)null : index, pointer.QueueIndex);
        Assert.Equal(6, pointer.End - pointer.Start);
        Assert.Equal(2, result.Steps.Count); // queued geometry after target is intentionally not decoded
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InlineGInfoGeometryUsesExistingPointerGrammarWithoutInventingDeferredPayload(bool extended)
    {
        var (bytes, _) = Block(4165, 0, Sweep(uint.MaxValue, 4, extended));
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        var pointer = Assert.Single(result.SimpleOwnTails).Geometry!;
        Assert.Equal((ushort)4, pointer.ClassId); Assert.Null(pointer.QueueIndex);
        Assert.Equal(extended ? 64 : 8, pointer.End - pointer.Start);
    }

    [Theory]
    [InlineData(500)] [InlineData(4165)]
    public void NonemptyReverseHistoryIsKeptAndDoesNotSelectALegacyEmptyTableTail(int cls)
    {
        var (bytes, _) = Block((ushort)cls, 0, cls == 500 ? BitConverter.GetBytes(-1) : Sweep(0, null), reverse: true);
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        var reverse = result.Steps[0].Tables[3];
        Assert.Equal(1u, reverse.Count); Assert.Equal(-1, Assert.Single(reverse.Entries).Id);
        Assert.Equal(new[] { 8, 2, -1, int.MinValue }, reverse.Entries[0].Keys);
    }

    [Theory]
    [InlineData(500, 0)] [InlineData(500, 3)]
    [InlineData(4165, 0)] [InlineData(4165, 3)] [InlineData(4165, 13)]
    public void TruncatedOwnFieldsLeaveCompletedPrefixAndNoPartialTail(int cls, int retained)
    {
        byte[] tail = cls == 500 ? BitConverter.GetBytes(1) : Sweep(0, null);
        var (bytes, end) = Block((ushort)cls, 0, tail[..retained], appendNext: false);
        var result = Read(bytes, out string? failure)!;
        Assert.False(result.Complete); Assert.Equal($"step-tail:2:{cls}", failure);
        Assert.Single(result.Steps); Assert.Empty(result.SimpleOwnTails); Assert.Equal(end, result.End);
    }

    [Fact]
    public void UnknownGeometryClassFailsWithoutScanningForAnotherBoundary()
    {
        var (bytes, end) = Block(4165, 0, Sweep(uint.MaxValue, ushort.MaxValue));
        var result = Read(bytes, out var failure)!;
        Assert.False(result.Complete); Assert.Equal("step-tail:2:4165", failure);
        Assert.Single(result.Steps); Assert.Empty(result.SimpleOwnTails); Assert.Equal(end, result.End);
    }

    [Theory]
    [InlineData(500)] [InlineData(4165)]
    public void LastOwnTailStillRemainsOutsideRequestedBasePrefix(int cls)
    {
        var (bytes, end) = Block((ushort)cls, -87, [], last: true);
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete); Assert.Single(result.Steps);
        Assert.Empty(result.SimpleOwnTails); Assert.Equal(end, result.End);
    }

    [Fact]
    public void FrameRelativeSpansAndArcWallRootRemainSupported()
    {
        var (bytes, _) = Block(4165, 0, Sweep(uint.MaxValue, 1736), root: 361);
        var expected = Read(bytes, out _);
        var embedded = new byte[bytes.Length + 19]; bytes.CopyTo(embedded, 7);
        var actual = TfBlockDeserializer.ExtractOwnWallGeometryHistories(embedded, 7,
            BitConverter.ToUInt32(bytes, 8), 361, out var failure);
        Assert.Null(failure); Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Theory]
    [InlineData(365)] [InlineData(2262)] [InlineData(4166)]
    public void SourceTailGuardDoesNotAdmitOtherConcreteClasses(int cls)
    {
        var method = typeof(TfBlockDeserializer).GetMethod("HasSimpleWallOwnTailSchema", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)method.Invoke(null, [(ushort)cls])!);
    }

    private static TfBlockDeserializer.OwnWallGeometryHistories? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallGeometryHistories(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out failure);

    private static byte[] Sweep(uint handle, ushort? cls, bool extended = false)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(handle);
        if (cls is ushort tag) writer.Write(tag);
        if (cls == 4)
        {
            writer.Write(extended ? (ushort)0x0108 : (ushort)8);
            if (extended) writer.Write(new byte[56]);
        }
        writer.Write(-1); writer.Write(int.MinValue); writer.Write((byte)0); writer.Write((byte)255);
        return stream.ToArray();
    }

    internal static (byte[] Bytes, int FirstPrefixEnd) Block(ushort cls, int version, byte[] tail,
        bool reverse = false, bool last = false, bool appendNext = true, ushort root = 3524)
    {
        var (original, offsets) = WallGeometryPhaseTests.Block(root);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(original.AsSpan(0, offsets["list"]));
        for (int phase = 0; phase < 6; phase++)
        {
            writer.Write(phase == 2 ? last ? 1u : 2u : 0u);
            if (phase != 2) continue;
            writer.Write(uint.MaxValue); writer.Write(cls);
            if (!last) { writer.Write(uint.MaxValue); writer.Write((ushort)365); }
        }
        writer.Write(original.AsSpan(offsets["list"] + 24, offsets["list-end"] - offsets["list"] - 24));
        Prefix(version, reverse); int end = (int)stream.Position;
        writer.Write(tail);
        if (!last && appendNext) Prefix(-87, false);
        writer.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return (bytes, end);

        void Prefix(int value, bool rows)
        {
            writer.Write(new byte[12]); writer.Write(rows ? 1u : 0u);
            if (rows) foreach (int key in new[] { -1, 8, 2, -1, int.MinValue }) writer.Write(key);
            writer.Write(17); writer.Write(value); writer.Write(int.MinValue); writer.Write(2u);
        }
    }
}
