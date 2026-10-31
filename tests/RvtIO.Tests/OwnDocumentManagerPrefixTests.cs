using System.Text;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnDocumentManagerPrefixTests
{
    private static readonly Guid Key = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Fact]
    public void RetainsEveryContentValueAndLocatesOnlyManagerOwnedUnitsTracking()
    {
        var fixture = Fixture(legacy: 4030);
        var result = Read(fixture.Data);
        Assert.NotNull(result); Assert.Equal(fixture.ManagerStart, result.ManagerStart);
        Assert.Equal(fixture.Data.Length, result.End);
        Assert.Equal(2, result.Header.AppInfoManagerQueueIndex);
        Assert.Equal(5, result.UnitsTrackingQueueIndex);
        Assert.Equal(new[] { 4, 5 }, result.OwnedAppInfos.Select(x => x.QueueIndex));
        var table = Assert.Single(result.ContentTables);
        Assert.Equal(1, table.QueueIndex);
        var record = Assert.Single(table.Records);
        Assert.Equal(Key, record.Key); Assert.Equal("автор \U0001F600\0", record.Author);
        Assert.Equal((-1, int.MinValue, int.MaxValue, 19), (record.OriginalElementId,
            record.CreationDate, record.LastModificationDate, record.LastUserModificationDate));
        Assert.Equal(new[] { new TfBlockDeserializer.DocumentEpisodeCount(-7, 0),
            new TfBlockDeserializer.DocumentEpisodeCount(91, int.MaxValue) }, record.EpisodeCounts);
        Assert.Equal(fixture.Positions["record"], record.Start);
        Assert.Equal(table.End, record.End); Assert.Equal(result.ManagerStart, table.End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void UnitsIndexFollowsActualArrayAndRootQueueOrder(int preceding)
    {
        var fixture = Fixture(children: [.. Enumerable.Repeat((ushort)27, preceding), 4030]);
        var result = Read(fixture.Data); Assert.NotNull(result);
        Assert.Equal(3 + preceding, result.UnitsTrackingQueueIndex);
    }

    [Fact]
    public void NullAndExternalPointersDoNotInventDeferredObjects()
    {
        var fixture = Fixture(children: [0, 0, 4030], handles: [0, 27, uint.MaxValue]);
        var result = Read(fixture.Data); Assert.NotNull(result);
        Assert.Equal(3, result.AppInfoArrayCount);
        var child = Assert.Single(result.OwnedAppInfos);
        Assert.Equal(2, child.ArrayIndex); Assert.Equal(3, child.QueueIndex);
    }

    [Theory]
    [InlineData(338)]
    [InlineData(821)]
    [InlineData(4)]
    [InlineData(65535)]
    public void WrongManagerArrayTypeRejects(int cls)
        => Assert.Null(Read(Fixture(children: [(ushort)cls, 4030]).Data));

    [Fact]
    public void MissingOrDuplicateOwnedTrackingRejectsEvenWithLegacyTracking()
    {
        Assert.Null(Read(Fixture(legacy: 4030, children: [27]).Data));
        Assert.Null(Read(Fixture(children: [4030, 4030]).Data));
        Assert.Null(Read(Fixture(children: []).Data));
    }

    [Theory]
    [InlineData(27)]
    [InlineData(4030)]
    public void SupportedLegacyRecordsAreParsedButNotSelected(int cls)
        => Assert.NotNull(Read(Fixture(legacy: (ushort)cls).Data));

    [Theory]
    [InlineData(338)]
    [InlineData(1357)]
    public void UnsupportedPreManagerRecordRejectsWithoutGuessedSkip(int cls)
        => Assert.Null(Read(Fixture(legacy: (ushort)cls).Data));

    [Theory]
    [InlineData("table")]
    [InlineData("record")]
    [InlineData("manager")]
    [InlineData("legacy")]
    public void ForeignOrNullDocumentBackReferencesReject(string field)
    {
        var fixture = Fixture(legacy: 4030);
        foreach (uint value in new uint[] { 0, 2, uint.MaxValue })
        {
            byte[] data = [.. fixture.Data]; BitConverter.GetBytes(value).CopyTo(data, fixture.Positions[field]);
            Assert.Null(Read(data));
        }
    }

    [Theory]
    [InlineData("records")]
    [InlineData("episodes")]
    [InlineData("children")]
    public void CountsAreBoundedBeforeAllocation(string field)
    {
        var fixture = Fixture();
        foreach (uint value in new uint[] { 100001, uint.MaxValue - 1 })
        {
            byte[] data = [.. fixture.Data]; BitConverter.GetBytes(value).CopyTo(data, fixture.Positions[field]);
            Assert.Null(Read(data));
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void EmptyContentAndEpisodeSentinelsAreAccepted(uint sentinel)
    {
        Assert.NotNull(Read(Fixture(emptyRecords: true, sentinel: sentinel).Data));
        var result = Read(Fixture(emptyEpisodes: true, sentinel: sentinel).Data);
        Assert.NotNull(result); Assert.Empty(Assert.Single(Assert.Single(result.ContentTables).Records).EpisodeCounts);
    }

    [Fact]
    public void StrictAuthorUtf16AndLengthBoundsReject()
    {
        var fixture = Fixture(); byte[] data = [.. fixture.Data];
        int length = fixture.Positions["author"];
        data[length + 4] = 0; data[length + 5] = 0xD8; Assert.Null(Read(data));
        BitConverter.GetBytes(10001u).CopyTo(data, length); Assert.Null(Read(data));
    }

    [Fact]
    public void EveryTruncatedPrefixRejectsAtomically()
    {
        var fixture = Fixture(legacy: 4030);
        for (int end = 0; end < fixture.Data.Length; end++) Assert.Null(Read(fixture.Data[..end]));
    }

    [Fact]
    public void DeferredTailIsNotReadAndWrongRootIsNotResynchronized()
    {
        var fixture = Fixture(); var result = Read([.. fixture.Data, 9, 8, 7]);
        Assert.NotNull(result); Assert.Equal(fixture.Data.Length, result.End);
        Assert.Null(Read([9, 8, .. fixture.Data]));
    }

    private static TfBlockDeserializer.DocumentManagerPrefix? Read(byte[] data)
        => TfBlockDeserializer.ExtractOwnDocumentManagerPrefix(data);

    private static (byte[] Data, int ManagerStart, Dictionary<string, int> Positions) Fixture(
        ushort? legacy = null, ushort[]? children = null, uint[]? handles = null,
        bool emptyRecords = false, bool emptyEpisodes = false, uint sentinel = 0)
    {
        children ??= [27, 4030]; var positions = new Dictionary<string, int>();
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Mark(string name) => positions.Add(name, (int)stream.Position);
        void Pointer(uint handle, ushort cls) { writer.Write(handle); if (handle != 0) writer.Write(cls); }
        writer.Write((ushort)28); Pointer(0, 0); writer.Write(legacy.HasValue ? 1u : 0u);
        if (legacy.HasValue) Pointer(uint.MaxValue, legacy.Value);
        Pointer(uint.MaxValue, 821); writer.Write(1u); Pointer(uint.MaxValue, 338);
        Pointer(uint.MaxValue, 3867); // remains deferred after manager; affects child FIFO indices
        for (int index = 0; index < 4; index++) Pointer(0, 0);
        writer.Write(-1); writer.Write(-1); writer.Write(17); writer.Write(2662);
        writer.Write(new byte[3]); writer.Write(0u); writer.Write(0u); Pointer(0, 0);
        if (legacy.HasValue)
        {
            Mark("legacy"); writer.Write(1u);
            if (legacy == 4030) writer.Write(999); // must not become current units
        }
        Mark("table"); writer.Write(1u); Mark("records"); writer.Write(emptyRecords ? sentinel : 1u);
        if (!emptyRecords)
        {
            Mark("record"); writer.Write(1u); writer.Write(Key.ToByteArray());
            string author = "автор \U0001F600\0"; Mark("author"); writer.Write((uint)author.Length);
            writer.Write(Encoding.Unicode.GetBytes(author));
            writer.Write(-1); writer.Write(int.MinValue); writer.Write(int.MaxValue); writer.Write(19);
            Mark("episodes"); writer.Write(emptyEpisodes ? sentinel : 2u);
            if (!emptyEpisodes) { writer.Write(-7); writer.Write(0); writer.Write(91); writer.Write(int.MaxValue); }
        }
        int managerStart = (int)stream.Position; Mark("manager"); writer.Write(1u);
        Mark("children"); writer.Write((uint)children.Length);
        for (int index = 0; index < children.Length; index++) Pointer(handles?[index] ?? uint.MaxValue, children[index]);
        return (stream.ToArray(), managerStart, positions);
    }
}
