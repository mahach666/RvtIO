using System.IO.Compression;
using OpenMcdf;

namespace RvtIO.Parsing;

/// <summary>
/// Exact decoder for the single gzip member streams of the Global and Formats storages: one
/// gzip member after an 8-byte prefix (Global streams) or at the start (Formats/Latest),
/// followed by unused trailing bytes.
///
/// A stream longer than one page carries a foreign 353-byte segment after every 64,896 bytes:
/// the segments start at stream offsets 64,896 + k * 65,249. Each one begins with a zero byte
/// followed by a byte whose low nibble is zero, like the removable segments of partition pages
/// (<see cref="ChunkDecompressor"/>). A short stream has no segment and is decoded unchanged.
/// The result is accepted only through <see cref="GZipStream"/>, which rejects a CRC-32 or
/// length mismatch, so a wrong page rule yields no data rather than wrong data.
/// </summary>
internal static class PagedGzipStream
{
    internal const int PageDataLength = 64_896;
    internal const int SegmentLength = 353;
    internal const int PageStride = PageDataLength + SegmentLength;
    /// <summary>Length of the prefix before the gzip member of a Global stream.</summary>
    internal const int PrefixLength = 8;

    /// <summary>Decoded bytes of <paramref name="storageName"/>/<paramref name="streamName"/>,
    /// or null when the stream is missing or does not decode exactly.</summary>
    internal static byte[]? Read(RootStorage root, string storageName, string streamName)
    {
        try
        {
            var storage = root.OpenStorage(storageName);
            using var stream = storage.OpenStream(streamName);
            var raw = new byte[stream.Length];
            stream.ReadExactly(raw);
            return Decompress(raw);
        }
        catch (Exception exception) when (exception is IOException or FileNotFoundException
            or DirectoryNotFoundException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Inflates the gzip member once every page segment is removed. Null when a
    /// segment is malformed, no deflate gzip header starts at offset 0 or 8, or the member does
    /// not match its own CRC-32 and length.</summary>
    internal static byte[]? Decompress(byte[] raw)
    {
        byte[]? body = RemoveSegments(raw);
        if (body == null) return null;
        int start = IsGzipHeader(body, 0) ? 0 : IsGzipHeader(body, PrefixLength) ? PrefixLength : -1;
        if (start < 0) return null;
        try
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(
                new MemoryStream(body, start, body.Length - start), CompressionMode.Decompress))
                gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool IsGzipHeader(byte[] data, int offset) =>
        data.Length >= offset + 18
        && data[offset] == 0x1F && data[offset + 1] == 0x8B && data[offset + 2] == 0x08;

    /// <summary>The stream without its page segments, or null when a segment position does
    /// not hold a segment (wrong tag, or the stream ends inside one).</summary>
    internal static byte[]? RemoveSegments(byte[] raw)
    {
        var body = new MemoryStream(raw.Length);
        int source = 0;
        for (long segment = PageDataLength; segment < raw.Length; segment += PageStride)
        {
            int start = (int)segment;
            if (start + SegmentLength > raw.Length
                || raw[start] != 0 || (raw[start + 1] & 0x0F) != 0)
                return null;
            body.Write(raw, source, start - source);
            source = start + SegmentLength;
        }
        body.Write(raw, source, raw.Length - source);
        return body.ToArray();
    }
}
