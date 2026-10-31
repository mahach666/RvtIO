using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// Per-element episode index: <see cref="ElemTableReader.ExtractEpisodeNumbers"/> reads offset+8 of the 28-byte R22 ElemTable record,
/// which the class layout (ElemRec/ElementHistory field list) and byte-size arithmetic identify as ElemRec.History.CreationDate.
///
/// This suite tests only the extraction mechanism, not a wired-up <c>ParsingState.ElemEpisodeNumbers</c>: the raw value needs a
/// per-document correction that the RVT file does not provide (see <see cref="ElemTableReader.LastEpisodeNumbers"/>), so the existing
/// consumption of <c>ElemEpisodeNumbers</c> in <c>BuildUniqueId</c> is left untouched.
/// </summary>
public class ElemTableEpisodeFieldTests
{
    private const uint Id = 12345;

    /// <summary>Builds one syntactically valid 28-byte R22 ElemTable record:
    /// [id:4][id:4][creationDate:4][lastModDate:4][lastUserModDate:4][partitionId:4][0xFFFFFFFF:4].
    /// The dup id pair at +0/+4 and the 0xFFFFFFFF tail at +24 are what
    /// <see cref="ElemTableReader.ExtractEpisodeNumbers"/> requires to accept a frame, as in the workset extraction.</summary>
    private static byte[] ValidRecord(uint id, int creationDate,
        uint lastModDate = 0, uint lastUserModDate = 0, uint partitionId = 0)
    {
        var bytes = new byte[28];
        BitConverter.GetBytes(id).CopyTo(bytes, 0);
        BitConverter.GetBytes(id).CopyTo(bytes, 4);
        BitConverter.GetBytes(creationDate).CopyTo(bytes, 8);
        BitConverter.GetBytes(lastModDate).CopyTo(bytes, 12);
        BitConverter.GetBytes(lastUserModDate).CopyTo(bytes, 16);
        BitConverter.GetBytes(partitionId).CopyTo(bytes, 20);
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(bytes, 24);
        return bytes;
    }

    [Fact]
    public void ReadsCreationDateAtOffset8()
    {
        var record = ValidRecord(Id, creationDate: 4253);
        var result = ElemTableReader.ExtractEpisodeNumbers(record, headerSize: 0);

        Assert.True(result.TryGetValue(Id, out int value));
        Assert.Equal(4253, value);
    }

    [Fact]
    public void DoesNotConfuseCreationDateWithNeighboringFields()
    {
        // the other fields carry different small values: extraction must read offset+8 specifically
        var record = ValidRecord(Id, creationDate: 64, lastModDate: 111, lastUserModDate: 222, partitionId: 333);
        var result = ElemTableReader.ExtractEpisodeNumbers(record, headerSize: 0);

        Assert.Equal(64, result[Id]);
    }

    [Fact]
    public void RejectsFrameWithoutDupIdPair()
    {
        // Offset+4 (m_history.m_originalElementId) differs from offset+0 (m_id), legitimate for a copied element, but the guarded
        // scan (as in ExtractWorksets) only trusts frames it can confirm and requires the dup pair.
        var record = ValidRecord(Id, creationDate: 64);
        BitConverter.GetBytes(Id + 1).CopyTo(record, 4);

        var result = ElemTableReader.ExtractEpisodeNumbers(record, headerSize: 0);

        Assert.Empty(result);
    }

    [Fact]
    public void RejectsFrameWithoutTailOrNextDupConfirmation()
    {
        var record = ValidRecord(Id, creationDate: 64);
        BitConverter.GetBytes(0x12345678u).CopyTo(record, 24); // not 0xFFFFFFFF, and no next record

        var result = ElemTableReader.ExtractEpisodeNumbers(record, headerSize: 0);

        Assert.Empty(result);
    }

    [Fact]
    public void AcceptsFrameConfirmedByNextRecordDupPairInsteadOfTail()
    {
        var first = ValidRecord(Id, creationDate: 64);
        BitConverter.GetBytes(0x12345678u).CopyTo(first, 24); // no FFFFFFFF tail this time
        var second = ValidRecord(Id + 1, creationDate: 65);
        var buffer = new byte[56];
        first.CopyTo(buffer, 0);
        second.CopyTo(buffer, 28);

        var result = ElemTableReader.ExtractEpisodeNumbers(buffer, headerSize: 0);

        Assert.Equal(64, result[Id]);
    }

    [Fact]
    public void RejectsNegativeValue()
    {
        var record = ValidRecord(Id, creationDate: -1);
        var result = ElemTableReader.ExtractEpisodeNumbers(record, headerSize: 0);
        Assert.Empty(result);
    }

    [Fact]
    public void RejectsImplausiblyLargeValue()
    {
        var record = ValidRecord(Id, creationDate: 100_000);
        var result = ElemTableReader.ExtractEpisodeNumbers(record, headerSize: 0);
        Assert.Empty(result);
    }

    [Fact]
    public void EmptyBuffer_YieldsEmpty_NoThrow()
    {
        var result = ElemTableReader.ExtractEpisodeNumbers(Array.Empty<byte>(), headerSize: 0);
        Assert.Empty(result);
    }

    [Fact]
    public void ElementsSharingAnEpisodeReadTheIdenticalRawValue()
    {
        // Three different elements, each its own 28-byte frame with a different id, two sharing creationDate=64 and one not:
        // elements of one episode read the identical raw value at this offset.
        var r1 = ValidRecord(100, creationDate: 64);
        var r2 = ValidRecord(200, creationDate: 64);
        var r3 = ValidRecord(300, creationDate: 65); // a different episode
        var buffer = new byte[84];
        r1.CopyTo(buffer, 0);
        r2.CopyTo(buffer, 28);
        r3.CopyTo(buffer, 56);

        var result = ElemTableReader.ExtractEpisodeNumbers(buffer, headerSize: 0);

        Assert.Equal(64, result[100]);
        Assert.Equal(64, result[200]);
        Assert.Equal(65, result[300]);
    }
}
