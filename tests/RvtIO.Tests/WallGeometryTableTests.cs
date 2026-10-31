using System.Text.Json;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallGeometryTableTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void OwnTableRetainsEveryScalarGeneratorSlotAndSpan(int cls)
    {
        var (bytes, start) = Block((ushort)cls, Table());
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        Assert.Equal(uint.MaxValue, result.GeometryTablePointer.Handle);
        Assert.Equal((ushort?)2087, result.GeometryTablePointer.ClassId);
        Assert.Equal(2, result.GeometryTablePointer.QueueIndex);
        var table = Assert.Single(result.GeometryTables);
        Assert.Equal(2, table.QueueIndex); Assert.Equal(uint.MaxValue, table.Handle);
        Assert.Equal(start, table.Start); Assert.Equal(start + 45, table.End);
        Assert.Equal(int.MaxValue, table.MaxSafeTag); Assert.Equal(int.MinValue, table.LastCheckedKingsUserModificationDate);
        Assert.Equal((byte)255, table.RefPointMirrored); // retained byte, not runtime cache state
        Assert.Equal(5u, table.EntryCount);
        Assert.Equal(new[] { -1, 0, 7, 7, int.MinValue }, table.Entries.Select(e => e.GeneratorId));
        Assert.Equal(Enumerable.Range(0, 5), table.Entries.Select(e => e.Index));
        Assert.Equal(start + 9, table.EntriesStart); Assert.Equal(start + 33, table.EntriesEnd);
        Assert.Equal(table.EntriesStart + 4, table.Entries[0].Start);
        Assert.All(table.Entries, e => Assert.Equal(4, e.End - e.Start));
        Assert.Equal(0u, table.BigTableOwner.Handle);
        Assert.All(new[] { table.MaterialMarkers, table.FaceTypeMarkers }, a => Assert.Empty(a.Members));
        Assert.Equal(table.Start, Assert.Single(result.PriorObjects).Start);
        Assert.Equal(table.End, result.PriorObjects[0].End);
        Assert.Equal(3, Assert.Single(result.Steps).QueueIndex);
        Assert.Equal(999, result.Steps[0].Id); // generator slots do not enqueue objects or replace step IDs
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void NullSentinelsRemainDistinctFromEmptyCollections(int index)
    {
        byte[] tail = Table(entries: []);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(tail, index == 0 ? 9 : index == 1 ? 17 : 21);
        var value = Assert.Single(Read(Block(3524, tail).Bytes, out _)!.GeometryTables);
        Assert.Equal(index == 0 ? uint.MaxValue : 0u, value.EntryCount);
        Assert.Equal(index == 1 ? uint.MaxValue : 0u, value.MaterialMarkers.Count);
        Assert.Equal(index == 2 ? uint.MaxValue : 0u, value.FaceTypeMarkers.Count);
        Assert.Empty(value.Entries);
        Assert.Empty(value.MaterialMarkers.Members); Assert.Empty(value.FaceTypeMarkers.Members);
    }

    [Fact]
    public void BigOwnerAndMarkerPointerSlotsKeepTheirRawKindsAndFifoIndices()
    {
        byte[] material = Pointer(0).Concat(Pointer(uint.MaxValue, 0))
            .Concat(Pointer(uint.MaxValue, 3905)).Concat(Pointer(42, 3905)).ToArray();
        byte[] face = Pointer(uint.MaxValue, 4).Concat(BitConverter.GetBytes((ushort)0x0108))
            .Concat(new byte[56]).ToArray();
        var result = Read(Block(3524, Table(big: Pointer(uint.MaxValue, 532),
            materialCount: 4, material: material, faceCount: 1, face: face)).Bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        var value = Assert.Single(result.GeometryTables);
        Assert.Equal((ushort?)532, value.BigTableOwner.ClassId); Assert.Equal(4, value.BigTableOwner.QueueIndex);
        Assert.Equal(new uint[] { 0, uint.MaxValue, uint.MaxValue, 42 }, value.MaterialMarkers.Members.Select(p => p.Handle));
        Assert.Equal(new int?[] { null, null, 5, 6 }, value.MaterialMarkers.Members.Select(p => p.QueueIndex));
        var inline = Assert.Single(value.FaceTypeMarkers.Members);
        Assert.Equal((ushort?)4, inline.ClassId); Assert.Null(inline.QueueIndex); Assert.Equal(64, inline.End-inline.Start);
        // Deferred big-table/marker bodies are after the requested last base prefix.
        Assert.Single(result.Steps); Assert.Single(result.PriorObjects);
    }

    [Theory]
    [InlineData(1u)] [InlineData(42u)] [InlineData(uint.MaxValue)]
    public void RootAndTableHandlesAreRetainedWithoutReferenceResolution(uint handle)
    {
        var result = Read(Block(3524, Table(), handle).Bytes, out var failure)!;
        Assert.Null(failure);
        Assert.Equal(handle, result.GeometryTablePointer.Handle);
        Assert.Equal(handle, Assert.Single(result.GeometryTables).Handle);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(16)] [InlineData(33)] [InlineData(36)]
    [InlineData(37)] [InlineData(38)] [InlineData(40)] [InlineData(44)]
    public void TruncatedFieldsDoNotPublishAPartialTableOrAdvanceItsAtomicBoundary(int length)
    {
        var (bytes, start) = Block(3524, Table()[..length], appendStep: false);
        AssertIncomplete(bytes, start);
    }

    [Theory]
    [InlineData(0, 100001u)] [InlineData(1, 100001u)] [InlineData(2, 100001u)]
    [InlineData(0, uint.MaxValue-1)] [InlineData(1, uint.MaxValue-1)] [InlineData(2, uint.MaxValue-1)]
    [InlineData(0, 4u)] [InlineData(1, 2u)] [InlineData(2, 1u)]
    public void UnboundedOrUnavailableEntriesAndPointersAreExplicitFailures(int field, uint count)
    {
        byte[] tail = Table(entries: []);
        int offset = field == 0 ? 9 : field == 1 ? 17 : 21;
        BitConverter.GetBytes(count).CopyTo(tail, offset);
        var (bytes, start) = Block(3524, tail, appendStep: false);
        AssertIncomplete(bytes, start);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void UnknownPointerTagsAreRejectedWithoutPromotingEarlierTableFields(int field)
    {
        byte[] bad = Pointer(uint.MaxValue, ushort.MaxValue);
        byte[] tail = field == 0 ? Table(big: bad) : field == 1 ? Table(materialCount: 1, material: bad)
            : Table(faceCount: 1, face: bad);
        var (bytes, start) = Block(3524, tail);
        AssertIncomplete(bytes, start);
    }

    [Fact]
    public void EmbeddedArcWallKeepsNestedSpansFrameRelative()
    {
        var (bytes, _) = Block(361, Table());
        var embedded = new byte[bytes.Length + 17]; bytes.CopyTo(embedded, 7);
        var value = TfBlockDeserializer.ExtractOwnWallGeometryHistories(embedded, 7,
            BitConverter.ToUInt32(bytes, 8), 361, out var failure);
        Assert.Null(failure);
        Assert.Equal(JsonSerializer.Serialize(Read(bytes, out _)), JsonSerializer.Serialize(value));
    }

    [Fact]
    public void NullRootGeometryPointerIsPreservedAndDoesNotInventATable()
    {
        var (bytes, _) = WallGeometryPhaseTests.Block(3524);
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete);
        Assert.Equal(0u, result.GeometryTablePointer.Handle); Assert.Null(result.GeometryTablePointer.QueueIndex);
        Assert.Empty(result.GeometryTables);
    }

    private static void AssertIncomplete(byte[] bytes, int start)
    {
        var value = Read(bytes, out var failure)!;
        Assert.Equal("geom-table:2:fields", failure); Assert.Equal(failure, value.Failure);
        Assert.False(value.Complete); Assert.Equal(start, value.End);
        Assert.Empty(value.GeometryTables); Assert.Empty(value.PriorObjects); Assert.Empty(value.Steps);
    }

    private static TfBlockDeserializer.OwnWallGeometryHistories? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallGeometryHistories(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out failure);

    private static byte[] Pointer(uint handle, ushort cls = 0)
        => handle == 0 ? BitConverter.GetBytes(handle) : BitConverter.GetBytes(handle).Concat(BitConverter.GetBytes(cls)).ToArray();

    private static byte[] Table(int[]? entries = null, byte[]? big = null,
        uint materialCount = 0, byte[]? material = null, uint faceCount = 0, byte[]? face = null)
    {
        entries ??= [-1, 0, 7, 7, int.MinValue];
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(int.MaxValue); writer.Write(int.MinValue); writer.Write((byte)255);
        writer.Write((uint)entries.Length); foreach (int value in entries) writer.Write(value);
        writer.Write(big ?? Pointer(0)); writer.Write(materialCount); writer.Write(material ?? []);
        writer.Write(faceCount); writer.Write(face ?? []); return stream.ToArray();
    }

    private static (byte[] Bytes, int TableStart) Block(ushort cls, byte[] table,
        uint handle = uint.MaxValue, bool appendStep = true)
    {
        var (original, offsets) = WallGeometryPhaseTests.Block(cls, geometryTablePointer: Pointer(handle, 2087));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(original.AsSpan(0, offsets["list"]));
        for (int phase = 0; phase < 6; phase++)
        {
            writer.Write(phase == 2 ? 1u : 0u);
            if (phase == 2) writer.Write(Pointer(uint.MaxValue, 4153));
        }
        writer.Write(original.AsSpan(offsets["list"] + 24, offsets["list-end"] - offsets["list"] - 24));
        int start = (int)stream.Position; writer.Write(table);
        if (appendStep)
        {
            writer.Write(new byte[16]); // four empty histories
            writer.Write(999); writer.Write(-87); writer.Write(int.MinValue); writer.Write(42u);
            writer.Write(new byte[] { 0xde, 0xad, 0xbe, 0xef }); // unparsed later graph
        }
        writer.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length-16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length-4);
        return (bytes, start);
    }
}
