using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallGeometryTableAddressingTests
{
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(int.MaxValue)]
    public void SmallTableBoundsUseArrayLengthNotMaxSafeTag(int maxSafe)
    {
        var value = Read(Block([-1, 7, int.MinValue], maxSafe: maxSafe), out var failure)!;
        Assert.Null(failure); Assert.Equal(2, value.MaxId); Assert.Equal(maxSafe, value.MaxSafeTag);
        Assert.Null(value.BigQueueIndex); Assert.Equal(new[] { -1, 7, int.MinValue }, value.Slots.Select(s => s.GeneratorId));
        Assert.Equal(Enumerable.Range(0, 3), value.Slots.Select(s => s.Id));
        Assert.All(value.Slots, s => { Assert.Equal("Small", s.Storage); Assert.Equal(2, s.StorageQueueIndex); });
    }

    [Fact]
    public void EmptySmallTableHasMaxIdMinusOneWithoutInventingAZeroSlot()
    {
        var value = Read(Block([]), out var failure)!;
        Assert.Null(failure); Assert.Equal(-1, value.MaxId); Assert.Empty(value.Slots);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void NonemptyBigArrayReplacesTheSmallTailButNotFilledPrefix(int bigCount)
    {
        int[] big = Enumerable.Range(0, bigCount).Select(i => 101 + i).ToArray();
        var value = Read(Block([88, 89], start: 4, filledBase: 2, big: big,
            chunks: [[-1, int.MaxValue], null]), out var failure)!;
        Assert.Null(failure); Assert.Equal(4 + (bigCount == 0 ? 2 : bigCount) - 1, value.MaxId);
        Assert.Equal(new[] { -1, int.MaxValue, -2, -2 }, value.Slots.Take(4).Select(s => s.GeneratorId));
        Assert.Equal(bigCount == 0 ? new[] { 88, 89 } : big, value.Slots.Skip(4).Select(s => s.GeneratorId));
        Assert.Equal(new[] { "Filled", "Filled", "Default", "Default" }, value.Slots.Take(4).Select(s => s.Storage));
        Assert.All(value.Slots.Skip(4), s => Assert.Equal(bigCount == 0 ? "Small" : "Big", s.Storage));
        Assert.All(value.Slots.Where(s => s.Storage == "Default"), s =>
        { Assert.Null(s.StorageQueueIndex); Assert.Null(s.Start); Assert.Null(s.End); });
    }

    [Fact]
    public void StartZeroNeedsNoFilledDivisionAndUsesNonemptyBigArray()
    {
        var value = Read(Block([900], start: 0, filledBase: 0, big: [5, -1], chunks: []), out var failure)!;
        Assert.Null(failure); Assert.Equal(1, value.MaxId);
        Assert.Equal(new[] { 5, -1 }, value.Slots.Select(s => s.GeneratorId));
        Assert.All(value.Slots, s => Assert.Equal("Big", s.Storage));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)]
    public void PrefixNeedsTheNativeFilledDivisorGreaterThanOne(int filledBase)
    {
        Assert.Null(Read(Block([], start: 2, filledBase: filledBase, big: [], chunks: [null]), out var failure));
        Assert.Equal("big-fields", failure);
    }

    [Fact]
    public void NegativeStartIsNotClampedIntoAnEmptyPrefix()
    {
        Assert.Null(Read(Block([], start: -1, filledBase: 2, big: [], chunks: []), out var failure));
        Assert.Equal("big-fields", failure);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrShortFilledBucketIsNotMistakenForANullBucket(bool shortChunk)
    {
        Assert.Null(Read(Block([], start: 4, filledBase: 2, big: [],
            chunks: shortChunk ? [[7], null] : [null]), out var failure));
        Assert.Equal(shortChunk ? "slot:1" : "slot:2", failure);
    }

    [Fact]
    public void AllGeneratorBitsDuplicatesAndMinusTwoAreRetainedWithoutActiveNodeInference()
    {
        var value = Read(Block([int.MinValue, int.MaxValue, -2, -1, 0, 0]), out var failure)!;
        Assert.Null(failure); Assert.Equal(new[] { int.MinValue, int.MaxValue, -2, -1, 0, 0 }, value.Slots.Select(s => s.GeneratorId));
        Assert.All(value.Slots, s => Assert.Equal("Small", s.Storage)); // stored -2 != missing bucket
    }

    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void SourceSpansPointToTheActualFrameBytesAcrossAllStorageKinds(int cls)
    {
        byte[] bytes = Block([77], start: 2, filledBase: 2, big: [42], chunks: [[8, 9]], root: (ushort)cls);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        foreach (var slot in value.Slots)
        {
            Assert.Equal(4, slot.End - slot.Start);
            Assert.Equal(slot.GeneratorId, BitConverter.ToInt32(bytes, slot.Start!.Value));
        }
    }

    [Fact]
    public void SerializedNullSmallCountIsExplicitlyUnresolvedRatherThanNormalized()
    {
        byte[] bytes = Block([]);
        var input = Complete(bytes);
        var history = input.Histories.Histories;
        var table = history.GeometryTables.Single() with { EntryCount = uint.MaxValue };
        input = input with { Histories = input.Histories with { Histories = history with { GeometryTables = [table] } } };
        Assert.Null(TfBlockDeserializer.SelectOwnWallGeometryTableAddressing(input, out var failure));
        Assert.Equal("small-count", failure);
    }

    [Fact]
    public void UnknownOrExternalBigOwnerRemainsUnresolved()
    {
        var input = Complete(Block([])); var history = input.Histories.Histories;
        var table = history.GeometryTables.Single();
        table = table with { BigTableOwner = table.BigTableOwner with { Handle = 42, ClassId = 0 } };
        input = input with { Histories = input.Histories with { Histories = history with { GeometryTables = [table] } } };
        Assert.Null(TfBlockDeserializer.SelectOwnWallGeometryTableAddressing(input, out var failure));
        Assert.Equal("big-owner", failure);
    }

    private static TfBlockDeserializer.OwnWallGeometryTableAddressing? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.SelectOwnWallGeometryTableAddressing(Complete(bytes), out failure);

    private static TfBlockDeserializer.OwnWallCompleteInputs Complete(byte[] bytes)
    {
        var input = TfBlockDeserializer.ExtractOwnWallCompleteInputs(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out var failure);
        Assert.Null(failure); return Assert.IsType<TfBlockDeserializer.OwnWallCompleteInputs>(input);
    }

    private static byte[] Block(int[] small, int? start = null, int filledBase = 0,
        int[]? big = null, int[]?[]? chunks = null, int maxSafe = -1, ushort root = 3524)
    {
        byte[] pointer = Bytes(w => { w.Write(uint.MaxValue); w.Write((ushort)2087); });
        var (original, offsets) = WallGeometryPhaseTests.Block(root, geometryTablePointer: pointer);
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(original.AsSpan(0, offsets["list"]));
        for (int phase = 0; phase < 6; phase++)
        { w.Write(phase == 0 ? 1u : 0u); if (phase == 0) { w.Write(uint.MaxValue); w.Write((ushort)500); } }
        w.Write(original.AsSpan(offsets["list"] + 24, offsets["list-end"] - offsets["list"] - 24));
        w.Write(maxSafe); w.Write(0); w.Write((byte)0); Ids(small);
        w.Write(start == null ? 0u : uint.MaxValue); if (start != null) w.Write((ushort)532);
        w.Write(0u); w.Write(0u); // material/face marker counts
        w.Write(new byte[16]); w.Write(999); w.Write(0); w.Write(0); w.Write(2u); w.Write(0); // BaseWall500
        if (start != null)
        {
            w.Write(start.Value); w.Write(filledBase); Ids(big ?? []);
            var values = chunks ?? []; w.Write((uint)values.Length);
            foreach (var value in values)
            { w.Write(value == null ? 0u : uint.MaxValue); if (value != null) w.Write((ushort)1859); }
            foreach (var value in values) if (value != null) Ids(value);
        }
        w.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return bytes;
        void Ids(int[] values) { w.Write((uint)values.Length); foreach (int value in values) w.Write(value); }
    }
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
}
