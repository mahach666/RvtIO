using System.IO.Compression;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class PagedGzipStreamTests
{
    private static byte[] Payload(int length)
    {
        var data = new byte[length];
        new Random(11).NextBytes(data);
        return data;
    }

    private static byte[] Member(byte[] payload)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.NoCompression, leaveOpen: true)) gzip.Write(payload);
        return stream.ToArray();
    }

    /// <summary>[8-byte prefix][member] with the page segment of the first page boundary inserted and
    /// <paramref name="tail"/> bytes of padding (not a segment) after the member.</summary>
    private static byte[] StreamWithSegment(byte[] member, int tail)
    {
        byte[] body = [.. new byte[PagedGzipStream.PrefixLength], .. member];
        var raw = new List<byte>(body.Take(PagedGzipStream.PageDataLength));
        raw.AddRange(new byte[PagedGzipStream.SegmentLength]);
        raw.AddRange(body.Skip(PagedGzipStream.PageDataLength));
        raw.AddRange(Enumerable.Repeat((byte)0x30, tail));
        return [.. raw];
    }

    [Fact]
    public void MemberEndingBeforeTheNextSegmentPosition_DecodesWithPaddingOverThatPosition()
    {
        byte[] payload = Payload(125_000);
        byte[] raw = StreamWithSegment(Member(payload), tail: 6_000);
        int secondSegment = PagedGzipStream.PageDataLength + PagedGzipStream.PageStride;
        Assert.True(raw.Length > secondSegment);
        Assert.Null(PagedGzipStream.RemoveSegments(raw));
        Assert.Equal(payload, PagedGzipStream.Decompress(raw));
    }

    [Fact]
    public void StreamWithoutItsFirstSegment_IsRejectedByTheMemberCrc()
    {
        byte[] raw = [.. new byte[PagedGzipStream.PrefixLength], .. Member(Payload(100_000))];
        Assert.Null(PagedGzipStream.Decompress(raw));
    }

    [Fact]
    public void MemberCutInsideItsSecondSegment_IsRejected()
    {
        byte[] raw = StreamWithSegment(Member(Payload(200_000)), tail: 0);
        byte[] cut = raw[..(PagedGzipStream.PageDataLength + PagedGzipStream.PageStride + 1)];
        Assert.Null(PagedGzipStream.Decompress(cut));
    }

    [Fact]
    public void MemberCutBeforeItsTrailer_IsRejected()
    {
        byte[] member = Member(Payload(1_000));
        Assert.Null(PagedGzipStream.Decompress(member[..^8]));
        Assert.NotNull(PagedGzipStream.Decompress(member));
    }
}
