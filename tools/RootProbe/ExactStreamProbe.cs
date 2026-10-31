using RvtIO.Parsing;

// Global/Formats streams use their own fixed page framing, not partition member discovery.
// Fail closed: lenient bytes must never stand in for a CRC-checked stream.
internal static class ExactStreamProbe
{
    internal static byte[] Decode(byte[] raw, string streamPath)
        => PagedGzipStream.Decompress(raw)
            ?? throw new InvalidDataException($"Stream '{streamPath}' failed exact paged-gzip validation.");
}
