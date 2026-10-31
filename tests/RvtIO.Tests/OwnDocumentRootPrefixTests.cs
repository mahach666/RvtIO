using System.Text;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnDocumentRootPrefixTests
{
    [Fact]
    public void CompleteHeaderRetainsTypedGuidCollectionAndExactStringValues()
    {
        Guid[] upgrades = [Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), Guid.Empty];
        string[] builds = ["Revit 2022", "Ревит \U0001F600", "", "unknown\0build"];
        byte[] data = Header(upgrades, builds);
        var prefix = Read(data);
        Assert.NotNull(prefix); Assert.Equal(data.Length, prefix.End);
        Assert.Equal((17, 2662), (prefix.DevBranchId, prefix.SyncVersion));
        Assert.Equal(upgrades, prefix.ExecutedUpgrades); Assert.Equal(builds, prefix.StoredByRevitBuild);
        Assert.Equal(1, prefix.AppInfoManagerQueueIndex);
        Assert.Equal(new TfBlockDeserializer.DocumentDeferredObject("m_pAppInfoManager", 338, uint.MaxValue), prefix.DeferredObjects[1]);
    }

    [Fact]
    public void ManagerIsOwnedByNamedFieldNotFirstClassOrHandleMatch()
    {
        var prefix = Read(Header([], [], legacyClass: 338));
        Assert.NotNull(prefix); Assert.Equal(2, prefix.AppInfoManagerQueueIndex);
        Assert.Equal(new[] { "m_appInfoArr", "m_oContentTable", "m_pAppInfoManager" }, prefix.DeferredObjects.Select(x => x.Field));
    }

    [Theory]
    [InlineData(0u, 338)]
    [InlineData(17u, 0)]
    public void NullOrExternalManagerHasNoInventedQueueOwner(uint handle, int tag)
    {
        var prefix = Read(Header([], [], managerHandle: handle, managerClass: (ushort)tag));
        Assert.NotNull(prefix); Assert.Null(prefix.AppInfoManagerQueueIndex);
        Assert.Single(prefix.DeferredObjects);
    }

    [Theory]
    [InlineData(339)]
    [InlineData(4030)]
    [InlineData(4)]
    [InlineData(65535)]
    public void WrongTypedManagerClassRejects(int tag)
        => Assert.Null(Read(Header([], [], managerClass: (ushort)tag)));

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void EmptyCollectionSentinelsHaveIdenticalSemantics(uint count)
    {
        byte[] data = Header([], []); BitConverter.GetBytes(count).CopyTo(data, 65);
        BitConverter.GetBytes(count).CopyTo(data, 69);
        var prefix = Read(data); Assert.NotNull(prefix);
        Assert.Empty(prefix.ExecutedUpgrades); Assert.Empty(prefix.StoredByRevitBuild);
    }

    [Fact]
    public void EveryTruncatedHeaderRejectsAtomically()
    {
        byte[] data = Header([Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")], ["build"]);
        for (int end = 0; end < data.Length; end++) Assert.Null(Read(data[..end]));
    }

    [Theory]
    [InlineData(65)]
    [InlineData(69)]
    [InlineData(6)]
    public void CountsAreBoundedBeforeAllocation(int position)
    {
        byte[] data = Header([], []); BitConverter.GetBytes(100001u).CopyTo(data, position);
        Assert.Null(Read(data));
        BitConverter.GetBytes(uint.MaxValue - 1).CopyTo(data, position); Assert.Null(Read(data));
    }

    [Fact]
    public void InvalidUtf16AndOversizedStringsReject()
    {
        byte[] data = Header([], ["x"]); data[77] = 0; data[78] = 0xD8;
        Assert.Null(Read(data));
        BitConverter.GetBytes(10001u).CopyTo(data, 73); Assert.Null(Read(data));
    }

    [Theory]
    [InlineData(62)]
    [InlineData(63)]
    [InlineData(64)]
    public void BooleanBytesAreCanonical(int offset)
    { byte[] data = Header([], []); data[offset] = 2; Assert.Null(Read(data)); }

    [Fact]
    public void HeaderEndDoesNotClaimDeferredGraphOrWholeStreamAcceptance()
    {
        byte[] header = Header([], []); var prefix = Read([.. header, 9, 8, 7]);
        Assert.NotNull(prefix); Assert.Equal(header.Length, prefix.End);
        Assert.Equal(2, prefix.DeferredObjects.Length);
    }

    [Fact]
    public void WrongRootTagDoesNotTriggerScanOrResynchronization()
    {
        byte[] header = Header([], []); Assert.Null(Read([9, 8, .. header]));
        header[0] = 27; Assert.Null(Read(header));
    }

    private static TfBlockDeserializer.DocumentRootPrefix? Read(byte[] data)
        => TfBlockDeserializer.ExtractOwnDocumentRootPrefix(data);

    private static byte[] Header(Guid[] upgrades, string[] builds, ushort? legacyClass = null,
        uint managerHandle = uint.MaxValue, ushort managerClass = 338)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Pointer(uint handle, ushort tag) { writer.Write(handle); if (handle != 0) writer.Write(tag); }
        writer.Write((ushort)28);
        Pointer(0, 0); // m_elemTable
        writer.Write(legacyClass.HasValue ? 1u : 0u);
        if (legacyClass.HasValue) Pointer(uint.MaxValue, legacyClass.Value);
        Pointer(uint.MaxValue, 821); // m_oContentTable
        writer.Write(1u); // m_pHostDocument, non-owning handle
        Pointer(managerHandle, managerClass);
        for (int index = 0; index < 5; index++) Pointer(0, 0);
        writer.Write(-1); writer.Write(-1); writer.Write(17); writer.Write(2662);
        writer.Write(new byte[3]); writer.Write((uint)upgrades.Length);
        foreach (Guid upgrade in upgrades) writer.Write(upgrade.ToByteArray());
        writer.Write((uint)builds.Length);
        foreach (string build in builds) { writer.Write((uint)build.Length); writer.Write(Encoding.Unicode.GetBytes(build)); }
        Pointer(0, 0); // m_oExServicesUsed
        return stream.ToArray();
    }
}
