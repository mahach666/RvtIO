using System.IO.Compression;
using System.Security.Cryptography;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnPartitionPartsTests
{
    private sealed record Map(bool Keyed = false, uint Owner = 42, ushort NodeClass = 3524,
        string? Fault = null, bool Split = false, bool Repeat = false);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectsWholeFramesAndChecksOtherMaps(bool split)
    {
        byte[] raw = Partition([new(Split: split), new(Keyed: true, Owner: 99)]);
        var parts = OwnPartitionParts.Read(raw, out var failure);
        Assert.Null(failure);
        var wall = Assert.Single(Assert.IsType<Dictionary<uint, OwnPartitionParts.WallParts>>(parts)).Value;
        Assert.Equal(SHA256.HashData(Record(42, 102, 3524)), wall.NodeSha256);
        Assert.Equal(SHA256.HashData(Record(42, 103, 2007)), wall.GeometrySha256);
    }

    [Fact]
    public void OwnerInAnotherMapIsNotAdmitted()
    {
        var parts = OwnPartitionParts.Read(Partition([new(), new(Keyed: true)]), out var failure);
        Assert.Null(failure);
        Assert.Empty(parts!);
    }

    [Fact]
    public void LastDepositReplacesPreviousClassAndDigest()
    {
        var parts = OwnPartitionParts.Read(Partition([new(Repeat: true)]), out var failure);
        Assert.Null(failure);
        Assert.Empty(parts!);
    }

    [Theory]
    [InlineData("crc")] [InlineData("isize")] [InlineData("footer")]
    [InlineData("record-bounds")] [InlineData("count")] [InlineData("body-sum")]
    [InlineData("checkback")] [InlineData("index")] [InlineData("continuation")]
    [InlineData("owner-count")] [InlineData("owner-set")]
    [InlineData("signature-bounds")] [InlineData("gzip-extra")] [InlineData("deflate")]
    [InlineData("unfinished-chain")] [InlineData("gzip-header")]
    public void CorruptionRejectsTheEntireResultIncludingEarlierValidMaps(string fault)
    {
        var parts = OwnPartitionParts.Read(Partition([new(), new(Keyed: true, Owner: 99, Fault: fault)]), out var failure);
        Assert.Null(parts);
        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Fact]
    public void KeyedFirstMapAndDuplicateKeysReject()
    {
        Assert.Null(OwnPartitionParts.Read(Partition([new(Keyed: true)]), out _));
        Assert.Null(OwnPartitionParts.Read(Partition([new(), new()]), out _));
        Assert.Null(OwnPartitionParts.Read(Partition([new(), new(Keyed: true), new(Keyed: true)]), out _));
    }

    [Fact]
    public void TruncationAtEveryByteRejectsUntilTheTerminalMarkerIsComplete()
    {
        byte[] raw = Partition([new(Split: true)]);
        for (int length = 0; length < raw.Length; length++)
            Assert.Null(OwnPartitionParts.Read(raw[..length], out _));
        // The explicit terminal marker ends maps; its trailing bytes are outside this parser.
        Assert.NotNull(OwnPartitionParts.Read([.. raw, 1, 2, 3], out _));
    }

    [Fact]
    public void PageFramingIsCheckedBeforeParsing()
    {
        byte[] clean = Partition([new()]);
        Array.Resize(ref clean, 70000);
        var raw = clean[..64896].Concat(new byte[353]).Concat(clean[64896..]).ToArray();
        Assert.NotNull(OwnPartitionParts.Read(raw, out _));
        raw[64896] = 1;
        Assert.Null(OwnPartitionParts.Read(raw, out _));
    }

    private static byte[] Record(uint owner, uint index, ushort kind, string? fault = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(owner);
        if (index != 101) writer.Write(77u);
        writer.Write(fault == "record-bounds" ? uint.MaxValue : 3u);
        writer.Write(kind);
        writer.Write((byte)17);
        writer.Write(fault == "footer" ? 4u : 3u);
        return stream.ToArray();
    }

    private static byte[] Partition(Map[] maps)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(9ul);
        foreach (var map in maps)
        {
            writer.Write((ushort)817);
            writer.Write(map.Keyed ? uint.MaxValue : 0u);
            if (map.Keyed) writer.Write((ushort)816);
            writer.Write(map.Fault == "owner-count" ? 2u : 1u);
            if (map.Keyed) writer.Write(new byte[16]);
            for (uint index = 101; index <= 103; index++)
            {
                uint owner = map.Fault == "owner-set" && index == 103 ? map.Owner + 1 : map.Owner;
                ushort kind = index == 102 ? map.NodeClass : index == 103 ? (ushort)2007 : (ushort)1;
                byte[] records = Record(owner, index, kind, map.Fault);
                uint count = 1, sum = 3;
                if (map.Repeat && index == 102)
                {
                    records = [.. records, .. Record(owner, index, 1)];
                    count++; sum += 3;
                }
                if (map.Split)
                {
                    Segment(writer, index, records[..5], 0, 0, 2, map.Fault);
                    Segment(writer, index, records[5..], count, sum, 1, map.Fault);
                }
                else Segment(writer, index, records, count, sum, 0, map.Fault);
            }
            writer.Write((ushort)3561);
            writer.Write(new byte[16]);
            writer.Write((ushort)3580);
            writer.Write(map.Fault == "signature-bounds" ? uint.MaxValue : 0u);
            writer.Write(0u);
        }
        writer.Write((ushort)817);
        writer.Write(0u);
        writer.Write(uint.MaxValue);
        return stream.ToArray();
    }

    private static void Segment(BinaryWriter writer, uint index, byte[] bytes, uint count,
        uint sum, uint mode, string? fault)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);
        byte[] payload = compressed.ToArray();
        payload[8] = 0;
        payload[9] = 0x0b; // The source's fixed gzip header; compressor's OS byte is otherwise platform-specific.
        if (fault == "crc") payload[^8] ^= 1;
        if (fault == "isize") payload[^4] ^= 1;
        if (fault == "gzip-header") payload[3] = 1;
        if (fault == "deflate") payload[10] = 7; // Reserved DEFLATE block type3.
        if (fault == "gzip-extra") payload = [.. payload[..^8], 0, .. payload[^8..]];
        uint size = (uint)payload.Length + 8;
        writer.Write((ushort)3561);
        writer.Write(fault == "continuation" ? 1u : fault == "unfinished-chain" ? 2u : mode);
        writer.Write(fault == "count" ? count + 1 : count);
        writer.Write(size);
        writer.Write(fault == "body-sum" ? sum + 1 : sum);
        writer.Write(fault == "index" ? 104u : index);
        writer.Write(19u);
        writer.Write(payload);
        writer.Write((ushort)3554);
        writer.Write(fault == "checkback" ? size + 1 : size);
    }
}
