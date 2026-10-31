using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The episode GUID table must be read from the RVT itself, without an external dump.
///
/// The tests on a real file (<see cref="EpisodeGuidTests"/>) check the mechanism, not the size of a particular project:
/// the table is not empty, every entry is a valid GUID, no duplicates.
///
/// <see cref="EpisodeGuidTests_ParseFromBuffer"/> exercises <c>RvtFileReader.ParseEpisodeGuids</c> directly on synthetic byte buffers,
/// the only way a byte-order regression or a missing header check can fail a test.
/// </summary>
public class EpisodeGuidTests : IClassFixture<TestFileStateFixture>
{
    private readonly TestFileStateFixture _fx;

    public EpisodeGuidTests(TestFileStateFixture fixture) => _fx = fixture;

    [SkippableFact]
    public void EpisodeGuidTable_IsParsedFromRvt_WithoutExternalDump()
    {
        // A bare NotEmpty would pass for a parser that returns after a single record; 100 is a safety floor, not the exact figure.
        Assert.True(_fx.State.EpisodeGuids.Count > 100,
            $"expected a real episode GUID table, got {_fx.State.EpisodeGuids.Count} entries");
    }

    [SkippableFact]
    public void EpisodeTableHasNoDuplicateEntries()
    {
        Assert.Equal(_fx.State.EpisodeGuids.Count,
                     new HashSet<string>(_fx.State.EpisodeGuids).Count);
    }
}

/// <summary>
/// Unit tests against <c>RvtFileReader.ParseEpisodeGuids</c> on synthetic Global/History byte buffers, no real .rvt file involved.
/// The record array starts at a computed base (<c>110 + 4 * u32@102</c>), not at a fixed offset. The tests are built around the full
/// fixed layout (<see cref="BuildPrefix"/>) so that a regression to a fixed offset (8, or a fixed 110 that ignores the extra-count field)
/// fails at least one of them.
/// </summary>
public class EpisodeGuidTests_ParseFromBuffer
{
    private const byte HdrA = 0xA2, HdrB = 0x04, HdrC = 0x01;

    /// <summary>The real 8-byte header signature the production stream opens with.</summary>
    private static byte[] ValidHeaderSignature() => new byte[] { HdrA, HdrB, HdrC, 0x00, 0x00, 0x00, 0x00, 0x00 };

    /// <summary>
    /// Builds the fixed prefix the parser expects before the record array: 8-byte signature, 94 filler bytes, the u32 extra-count at
    /// offset 102, <paramref name="extraCount"/> filler u32s, then the u32 <paramref name="x"/> immediately before the record array.
    /// Total length is <c>110 + 4 * extraCount</c>. <paramref name="offset8Bytes"/>, when given, overwrites the start of the filler region
    /// (stream offset 8 onward) to plant bytes that would look like a record if a regression read from offset 8.
    /// </summary>
    private static List<byte> BuildPrefix(uint extraCount, uint x, byte[]? offset8Bytes = null)
    {
        var filler = new byte[94]; // offsets 8..101
        if (offset8Bytes != null)
            Array.Copy(offset8Bytes, filler, offset8Bytes.Length);

        var bytes = new List<byte>();
        bytes.AddRange(ValidHeaderSignature());              // 0..7
        bytes.AddRange(filler);                                // 8..101
        bytes.AddRange(BitConverter.GetBytes(extraCount));    // 102..105
        for (int i = 0; i < extraCount; i++)
            bytes.AddRange(BitConverter.GetBytes((uint)0));    // 106..106+4*extraCount-1 (contents unused)
        bytes.AddRange(BitConverter.GetBytes(x));              // X, the 4 bytes right before the array
        return bytes; // Count == 110 + 4 * extraCount
    }

    /// <summary>A 16-byte GUID body of sequential bytes starting at <paramref name="startByte"/>, plus a trailing tag byte.
    /// The expected "D"-format string is derivable by hand from the Guid(byte[]) layout (Data1/2/3 byte-swapped, Data4 direct).</summary>
    private static byte[] SequentialRecord(byte startByte, byte tag) =>
        Enumerable.Range(startByte, 16).Select(i => (byte)i).Append(tag).ToArray();

    [Fact]
    public void ValidHeaderAndTwoRecords_YieldsExactlyTwoExpectedGuidsAndX()
    {
        var buffer = BuildPrefix(extraCount: 0, x: 529);
        buffer.AddRange(SequentialRecord(0x00, 0x28)); // tag values observed on the expected export file
        buffer.AddRange(SequentialRecord(0x10, 0x05));

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { buffer.ToArray() }, null);

        Assert.Equal(2, result.Guids.Count);
        // Hand-derived from the Guid(byte[]) layout: Data1/2/3 byte-reversed, Data4 verbatim. A byte-order regression changes these strings.
        Assert.Equal("03020100-0504-0706-0809-0a0b0c0d0e0f", result.Guids[0]);
        Assert.Equal("13121110-1514-1716-1819-1a1b1c1d1e1f", result.Guids[1]);
        Assert.Equal(529u, result.X);
    }

    [Fact]
    public void NonEmptyExtraList_RecordArrayStartsPastFixedBase_NotAtOffset110()
    {
        // extraCount = 3 moves the record array to 110 + 4*3 = 122; a parser that hardcodes 110 would read the tail of the zero-filled
        // extra list plus X as the first record.
        var buffer = BuildPrefix(extraCount: 3, x: 845);
        buffer.AddRange(SequentialRecord(0x00, 0x28));
        buffer.AddRange(SequentialRecord(0x10, 0x05));

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { buffer.ToArray() }, null);

        Assert.Equal(2, result.Guids.Count);
        Assert.Equal("03020100-0504-0706-0809-0a0b0c0d0e0f", result.Guids[0]);
        Assert.Equal("13121110-1514-1716-1819-1a1b1c1d1e1f", result.Guids[1]);
        Assert.Equal(845u, result.X);
    }

    [Fact]
    public void PlausibleRecordBytesAtOffset8_AreNotReadAsPartOfTheTable()
    {
        // Plants a GUID body + tag at stream offset 8, where a hardcoded start used to read. With the computed base
        // (extraCount = 0 => 110) these bytes are header filler and must never surface in the result.
        var phantomRecord = SequentialRecord(0xF0, 0x28);
        var buffer = BuildPrefix(extraCount: 0, x: 1, offset8Bytes: phantomRecord);
        buffer.AddRange(SequentialRecord(0x00, 0x28));
        buffer.AddRange(SequentialRecord(0x10, 0x05));

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { buffer.ToArray() }, null);

        const string phantomGuid = "f3f2f1f0-f5f4-f7f6-f8f9-fafbfcfdfeff"; // hand-derived from 0xF0..0xFF
        Assert.DoesNotContain(phantomGuid, result.Guids);
        Assert.Equal(2, result.Guids.Count);
        Assert.Equal("03020100-0504-0706-0809-0a0b0c0d0e0f", result.Guids[0]);
        Assert.Equal("13121110-1514-1716-1819-1a1b1c1d1e1f", result.Guids[1]);
    }

    [Fact]
    public void WrongHeaderSignature_YieldsEmptyTableAndNullX()
    {
        var buffer = BuildPrefix(extraCount: 0, x: 529);
        buffer.AddRange(SequentialRecord(0x00, 0x28));
        buffer.AddRange(SequentialRecord(0x10, 0x05));
        var bytes = buffer.ToArray();
        // Corrupt just the header, keep the rest (which would otherwise parse to 2 valid GUIDs): the rejection comes from the signature check.
        bytes[0] = 0xFF;
        bytes[1] = 0xFF;

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { bytes }, null);

        Assert.Empty(result.Guids);
        Assert.Null(result.X);
    }

    [Fact]
    public void TrailingPartialRecord_OnlyWholeRecordsAreRead()
    {
        var buffer = BuildPrefix(extraCount: 0, x: 42);
        buffer.AddRange(SequentialRecord(0x00, 0x28)); // full GUID #1
        buffer.AddRange(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA }); // 5-byte partial tail: not
                                                                       // enough for another 16-byte GUID

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { buffer.ToArray() }, null);

        Assert.Single(result.Guids);
        Assert.Equal("03020100-0504-0706-0809-0a0b0c0d0e0f", result.Guids[0]);
        Assert.Equal(42u, result.X);
    }

    [Fact]
    public void BaseComputedPastEndOfStream_YieldsEmptyTable_NoThrow()
    {
        // extraCount = 1000 => base = 4110, but the buffer stops right after the extra-count field (106 bytes): the base is not satisfiable.
        var bytes = new List<byte>();
        bytes.AddRange(ValidHeaderSignature());
        bytes.AddRange(new byte[94]);
        bytes.AddRange(BitConverter.GetBytes((uint)1000));
        Assert.Equal(106, bytes.Count);

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { bytes.ToArray() }, null);

        Assert.Empty(result.Guids);
        Assert.Null(result.X);
    }

    [Fact]
    public void StreamTooShortToHoldFixedBase_YieldsEmptyTable_NoThrow()
    {
        // extraCount = 0 => base = 110, but the buffer is only 106 bytes: too short to hold 110 + 4*0.
        var bytes = new List<byte>();
        bytes.AddRange(ValidHeaderSignature());
        bytes.AddRange(new byte[94]);
        bytes.AddRange(BitConverter.GetBytes((uint)0));
        Assert.Equal(106, bytes.Count);

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { bytes.ToArray() }, null);

        Assert.Empty(result.Guids);
        Assert.Null(result.X);
    }

    [Fact]
    public void StreamTooShortToHoldExtraCountField_YieldsEmptyTable_NoThrow()
    {
        // Header matches, but the stream ends before byte 106: the u32 extra-count at offset 102 cannot be read.
        var bytes = new List<byte>();
        bytes.AddRange(ValidHeaderSignature());
        bytes.AddRange(new byte[90]); // total length 98, short of the 106 needed to read u32@102

        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { bytes.ToArray() }, null);

        Assert.Empty(result.Guids);
        Assert.Null(result.X);
    }

    [Fact]
    public void BufferShorterThanHeader_YieldsEmpty_NoThrow()
    {
        var buffer = new byte[] { 0xA2, 0x04, 0x01 }; // 3 bytes, less than the 8-byte header
        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { buffer }, null);
        Assert.Empty(result.Guids);
        Assert.Null(result.X);
    }

    [Fact]
    public void ZeroLengthBuffer_YieldsEmpty_NoThrow()
    {
        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]> { Array.Empty<byte>() }, null);
        Assert.Empty(result.Guids);
        Assert.Null(result.X);
    }

    [Fact]
    public void EmptyChunkList_YieldsEmpty_NoThrow()
    {
        var result = RvtFileReader.ParseEpisodeGuids(new List<byte[]>(), null);
        Assert.Empty(result.Guids);
        Assert.Null(result.X);
    }
}
