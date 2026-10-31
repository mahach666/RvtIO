using System.IO.Compression;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class RootProbeExactStreamTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void ExactMember_PreservesRawInputAndAllDecodedBytes(int prefix)
    {
        byte[] data = [28, 0, 1, 2, 3, 4, 5, 6, 7];
        byte[] raw = [.. new byte[prefix], .. Gzip(data), .. new byte[48]];
        byte[] copy = raw.ToArray();
        Assert.Equal(data, ExactStreamProbe.Decode(raw, "Global/Latest"));
        Assert.Equal(copy, raw);
    }

    [Theory]
    [InlineData(0)] // CRC32
    [InlineData(4)] // ISIZE
    public void FinishedDeflateWithWrongTrailer_ThrowsInsteadOfReturningLenientBytes(int trailerOffset)
    {
        byte[] data = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        byte[] member = Gzip(data);
        member[member.Length - 8 + trailerOffset] ^= 1;
        byte[] raw = [.. new byte[8], .. member, .. new byte[64]];
        var exception = Assert.Throws<InvalidDataException>(() => ExactStreamProbe.Decode(raw, "Global/Latest"));
        Assert.Contains("Global/Latest", exception.Message);
    }

    [Fact]
    public void PagedMember_UsesExistingExactFraming_NotPartitionMarkerDiscovery()
    {
        byte[] data = new byte[3 * PagedGzipStream.PageDataLength];
        new Random(77).NextBytes(data);
        byte[] body = [.. new byte[8], .. Gzip(data), .. new byte[48]];
        byte[] raw = InsertSegments(body);
        Assert.Equal(data, ExactStreamProbe.Decode(raw, "Global/Latest"));

        raw[PagedGzipStream.PageDataLength] = 1;
        Assert.Throws<InvalidDataException>(() => ExactStreamProbe.Decode(raw, "Global/Latest"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void MissingOrTruncatedMember_Throws(int size)
        => Assert.Throws<InvalidDataException>(() => ExactStreamProbe.Decode(new byte[size], "Global/Latest"));

    [Fact]
    public void GzipMarkerAtUnframedOffset_IsNotResynchronized()
    {
        byte[] raw = [1, 2, 3, .. Gzip([28, 0, 1, 2, 3])];
        Assert.Throws<InvalidDataException>(() => ExactStreamProbe.Decode(raw, "Global/Latest"));
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] InsertSegments(byte[] data)
    {
        using var raw = new MemoryStream();
        for (int start = 0; start < data.Length; start += PagedGzipStream.PageDataLength)
        {
            int count = Math.Min(data.Length - start, PagedGzipStream.PageDataLength);
            raw.Write(data, start, count);
            if (count == PagedGzipStream.PageDataLength && start + count < data.Length)
                raw.Write(new byte[PagedGzipStream.SegmentLength]);
        }
        return raw.ToArray();
    }
}
