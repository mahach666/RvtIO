using System.IO.Compression;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class ElemTableRecoverTests
{
    [Fact]
    public void Recover_ValidGzip_ReturnsCrcValidatedPayload()
    {
        byte[] payload = Enumerable.Range(0, 4096).Select(i => (byte)(i * 31)).ToArray();
        byte[] gzip = Compress(payload);

        byte[]? recovered = ElemTableRecover.Recover(gzip, 0);

        Assert.Equal(payload, recovered);
    }

    /// <summary>Pins the trailer location: the OLE `Global/ElemTable` stream is `[8-byte header][gzip member][97-605 trailing bytes]`.
    /// The last 8 bytes of the whole buffer are crafted to look like a plausible trailer (a CRC- and size-shaped pair that does not
    /// belong to the payload), so reading the tail of the raw stream would mis-locate the trailer; it must be found at the true end of
    /// the gzip member (the inflater's own BFINAL).</summary>
    [Fact]
    public void Recover_TrailerLocatedPastTrailingBytes_IgnoresDeceptiveTailBytes()
    {
        byte[] payload = Enumerable.Range(0, 4096).Select(i => (byte)(i * 37)).ToArray();
        byte[] gzip = Compress(payload);

        const int oleHeaderLen = 8;
        byte[] trailingJunk = new byte[137];
        for (int i = 0; i < trailingJunk.Length; i++) trailingJunk[i] = (byte)(0x40 + i % 17);
        // The last 8 bytes of the trailing region parse as a plausible trailer (CRC then size) if read from the end of the raw stream;
        // they belong to neither the payload nor anything real.
        WriteUInt32(trailingJunk, trailingJunk.Length - 8, 0xDEADBEEF);
        WriteUInt32(trailingJunk, trailingJunk.Length - 4, (uint)(payload.Length * 7));

        byte[] raw = new byte[oleHeaderLen + gzip.Length + trailingJunk.Length];
        for (int i = 0; i < oleHeaderLen; i++) raw[i] = 0xEE;
        Buffer.BlockCopy(gzip, 0, raw, oleHeaderLen, gzip.Length);
        Buffer.BlockCopy(trailingJunk, 0, raw, oleHeaderLen + gzip.Length, trailingJunk.Length);

        uint trueCrc = BitConverter.ToUInt32(gzip, gzip.Length - 8);
        uint trueIsize = BitConverter.ToUInt32(gzip, gzip.Length - 4);
        int expectedTrailerOffset = oleHeaderLen + gzip.Length - 8;

        byte[]? recovered = ElemTableRecover.Recover(raw, oleHeaderLen, out var outcome);

        Assert.Equal(payload, recovered);
        Assert.True(outcome.Accepted);
        Assert.Equal(0, outcome.DesyncCount);
        Assert.Equal(expectedTrailerOffset, outcome.TrailerOffset);
        Assert.Equal(trueCrc, outcome.TrueCrc);
        Assert.Equal(trueIsize, outcome.TrueIsize);
        Assert.NotEqual(0xDEADBEEFu, outcome.TrueCrc);
        Assert.NotEqual((uint)(payload.Length * 7), outcome.TrueIsize);
    }

    /// <summary>The output cap must not be derived from the gzip trailer's declared ISIZE. Only ISIZE is corrupted here (far smaller
    /// than the true payload), the CRC is intact: the cap is a fixed safety constant and acceptance is decided by CRC alone, so a
    /// lying ISIZE must not block a correct recovery.</summary>
    [Fact]
    public void Recover_DeclaredIsizeLies_AcceptsOnCrcMatchRegardlessOfDeclaredSize()
    {
        byte[] payload = Enumerable.Range(0, 4096).Select(i => (byte)(i * 17)).ToArray();
        byte[] gzip = Compress(payload);
        WriteUInt32(gzip, gzip.Length - 4, 1);   // declared ISIZE now lies: says 1, really 4096

        byte[]? recovered = ElemTableRecover.Recover(gzip, 0, out var outcome);

        Assert.Equal(payload, recovered);
        Assert.True(outcome.Accepted);
        Assert.Equal(1u, outcome.TrueIsize);              // the lying value, read faithfully...
        Assert.Equal(payload.Length, outcome.RecoveredLength); // ...but never used to cap output
    }

    [Fact]
    public void Recover_CrcDoesNotMatch_ReturnsNullAndReportsTruthfulRejection()
    {
        byte[] payload = Enumerable.Range(0, 4096).Select(i => (byte)(i * 13)).ToArray();
        byte[] gzip = Compress(payload);
        uint trueIsize = BitConverter.ToUInt32(gzip, gzip.Length - 4);
        gzip[^8] ^= 0x01;
        uint corruptedCrc = BitConverter.ToUInt32(gzip, gzip.Length - 8);

        byte[]? recovered = ElemTableRecover.Recover(gzip, 0, out var outcome);

        Assert.Null(recovered);
        Assert.False(outcome.Accepted);
        Assert.True(outcome.TrailerFound);
        Assert.Equal(payload.Length, outcome.RecoveredLength);
        Assert.Equal(0, outcome.DesyncCount);
        Assert.Equal(corruptedCrc, outcome.TrueCrc);   // reports what the trailer actually says
        Assert.Equal(trueIsize, outcome.TrueIsize);
        Assert.Contains("CRC mismatch", outcome.Reason);
    }

    [Fact]
    public void ByteBuf_RejectsWritePastExactLimit()
    {
        var buffer = new ElemTableRecover.ByteBuf(cap: 2, maxCount: 4);
        buffer.AddRange([1, 2, 3, 4], 0, 4);

        Assert.Equal(4, buffer.Count);
        Assert.Equal(4, buffer.Arr.Length);
        Assert.Throws<ElemTableRecover.OutputLimitExceededException>(() => buffer.Add(5));
        Assert.Equal(4, buffer.Count);
    }

    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(payload);
        return output.ToArray();
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
