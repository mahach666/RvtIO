using RvtIO.Parsing;
using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class WallDefaultClearingTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectsBaseWallOrHelperExtrusionFromTheOwnFormListWithoutLookingUpSketch(bool helper)
    {
        var value = Read(Block(helper: helper));
        Assert.Equal(helper, value.ExtrusionHelperQueueIndex != null);
        var callback = Assert.Single(value.Callbacks, c => c.ClassId == 596);
        Assert.Equal((ushort?)(helper ? 1502 : 500), callback.GeneratorClassId);
        Assert.Equal(new[] { 1, 2 }, callback.Sides.Select(s => s.Id));
        Assert.Equal(new[] { 0, 1 }, callback.Sides.Select(s => s.HistoryRowIndex!.Value));
        Assert.Equal(new[] { 1, -1, -1 }, callback.Sides[0].HistoryKeys);
        Assert.Equal(new[] { 2, -1, -1 }, callback.Sides[1].HistoryKeys);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(2, false)] [InlineData(0, true)] [InlineData(2, true)]
    public void SlantedAndTaperedSidesUseDifferentKeysUnlessElevationHelperExists(int cross, bool helper)
    {
        var keys0 = helper ? new[] { 1, -1, -1 } : new[] { 3, 3, 0 };
        var keys1 = helper ? new[] { 2, -1, -1 } : new[] { 3, 1, 0 };
        var value = Read(Block(cross: cross, helper: helper, histories: [(1, keys0), (2, keys1)]));
        var callback = Assert.Single(value.Callbacks, c => c.ClassId == 596);
        Assert.Equal(keys0, callback.Sides[0].HistoryKeys); Assert.Equal(keys1, callback.Sides[1].HistoryKeys);
        Assert.Equal(new[] { 1, 2 }, callback.Sides.Select(s => s.Id));
    }

    [Theory]
    [InlineData(-1)] [InlineData(3)] [InlineData(int.MaxValue)]
    public void UnknownCrossSectionWithoutHelperReturnsNoSideIds(int cross)
    {
        var value = Read(Block(cross: cross));
        Assert.Null(Assert.Single(value.Callbacks, c => c.ClassId == 596).GeneratorQueueIndex);
        Assert.All(Assert.Single(value.Callbacks, c => c.ClassId == 596).Sides, s =>
        { Assert.Equal(-1, s.Id); Assert.Equal(new[] { -1, -1, -1 }, s.HistoryKeys); });
    }

    [Theory]
    [InlineData(0)] [InlineData(0x100000)] [InlineData(-1)]
    public void Callback36SkipFlagDoesNotSkipCallback37(int flags)
    {
        var value = Read(Block(flags: flags));
        var callback = Assert.Single(value.Callbacks, c => c.ClassId == 596);
        Assert.Equal(flags, callback.Flags); Assert.Equal(2, callback.Sides.Count);
    }

    [Fact]
    public void ZeroLocalGeometrySkipsEveryCallbackAndItsDependencies()
    {
        var value = Read(Block(), 0);
        Assert.Empty(value.Callbacks); Assert.Null(value.WallCrossSection); Assert.Null(value.ExtrusionHelperQueueIndex);
    }

    [Fact]
    public void MissingGeneratorOrMissingHistoryKeyReturnsMinusOneRatherThanAFixedTag()
    {
        Assert.All(Assert.Single(Read(Block(generator: false)).Callbacks, c => c.ClassId == 596).Sides, s => Assert.Equal(-1, s.Id));
        var value = Read(Block(histories: [(123, [3, 0, -1])]));
        Assert.All(Assert.Single(value.Callbacks, c => c.ClassId == 596).Sides, s => Assert.Equal(-1, s.Id));
    }

    [Fact]
    public void DuplicateHistoryKeysRemainUnresolvedNotFirstOrLastWinner()
    {
        var (input, references) = Inputs(Block(histories: [(1, [1, -1, -1]), (2, [1, -1, -1])]));
        Assert.Null(SelectOwnWallDebugOffClearIds(input, references, 1, out var failure));
        Assert.StartsWith("clear-face-history:", failure);
    }

    [Fact]
    public void ClearingPreservesColumnsAndRepeatedIdsIncludingNullUpdates()
    {
        var ids = Read(Block(histories: [(1, [1, -1, -1]), (1, [2, -1, -1])]));
        var prefix = Physical();
        var result = ApplyOwnWallDebugOffClearing(prefix, ids, out var failure)!;
        Assert.Null(failure); Assert.Equal(2, result.Visits.Count);
        Assert.NotNull(result.Visits[0].Previous); Assert.Null(result.Visits[1].Previous);
        Assert.Null(result.Slots[1].Node); Assert.Equal(prefix.Slots.Select(s => s.Persisted), result.Slots.Select(s => s.Persisted));
        Assert.Equal(prefix.Slots[2].Node, result.Slots[2].Node);
    }

    [Theory]
    [InlineData(-2)] [InlineData(4)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void OnlyMinusOneIsOmittedByCallbackOtherInvalidIdsAreRetainedAndRejectedByBounds(int id)
    {
        var ids = Read(Block(histories: [(id, [1, -1, -1]), (-1, [2, -1, -1])]));
        var result = ApplyOwnWallDebugOffClearing(Physical(), ids, out var failure)!;
        Assert.Null(failure); var visit = Assert.Single(result.Visits);
        Assert.Equal(id, visit.Id); Assert.Equal("out-of-range", visit.Outcome);
        Assert.All(result.Slots, s => Assert.NotNull(s.Node));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NoClearingOrAllMinusOneIdsKeepsThePhysicalPrefix(bool noGeometry)
    {
        var ids = Read(Block(histories: []), noGeometry ? 0 : 1);
        var prefix = Physical() with { GeometryCount = noGeometry ? 0 : 1 };
        var value = ApplyOwnWallDebugOffClearing(prefix, ids, out var failure)!;
        Assert.Null(failure); Assert.Empty(value.Visits); Assert.Equal(prefix.Slots, value.Slots);
    }

    [Theory]
    [InlineData("owner")] [InlineData("count")] [InlineData("bounds")]
    public void IncompatiblePhysicalPrefixIsNotCleared(string mutation)
    {
        var ids = Read(Block()); var prefix = Physical();
        prefix = mutation switch { "owner" => prefix with { OwnerId = 101 }, "count" => prefix with { GeometryCount = 2 }, _ => prefix with { MaxId = 2 } };
        Assert.Null(ApplyOwnWallDebugOffClearing(prefix, ids, out var failure)); Assert.Equal("clear-table-owner", failure);
    }

    private static OwnWallDebugOffClearIds Read(byte[] bytes, int geometries = 1)
    {
        var (input, references) = Inputs(bytes);
        var value = SelectOwnWallDebugOffClearIds(input, references, geometries, out var failure);
        Assert.Null(failure); return Assert.IsType<OwnWallDebugOffClearIds>(value);
    }
    private static (OwnWallCompleteInputs, OwnWallReferenceGeometryInputs) Inputs(byte[] bytes)
    {
        uint size = BitConverter.ToUInt32(bytes, 8);
        var input = ExtractOwnWallCompleteInputs(bytes, 0, size, 3524, out var failure)!; Assert.Null(failure);
        var references = ExtractOwnWallReferenceGeometryInputs(bytes, 0, size, 3524, out failure)!; Assert.Null(failure);
        return (input, references);
    }
    private static OwnWallPhysicalTablePrefix Physical() => new(100, 3524, 3, 1, 0, [],
        Enumerable.Range(0, 4).Select(i => new WallPhysicalTableSlot(new(i, -2, "Default", null, i, null, null),
            new("GRep", i, 1589, i, uint.MaxValue, 0, 80))).ToArray());

    private static byte[] Block(int cross = 1, bool helper = false, bool generator = true, int flags = 0,
        (int Id, int[] Keys)[]? histories = null)
    {
        histories ??= [(1, [1, -1, -1]), (2, [2, -1, -1])];
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream); var queue = new Queue<Action>();
        w.Write(100); w.Write(0u); w.Write(0u); w.Write((ushort)3524);
        foreach (var f in SerializationFields(3524)!)
            if (f.Declaring == 37 && f.Name == "m_id") w.Write(100);
            else if (f.Declaring == 37 && f.Name == "m_docAccess") w.Write(1u);
            else if (f.Declaring == 362 && f.Name == "m_wallCrossSection") w.Write(cross);
            else if (f.Name == "m_geomSteps") Pointer(2086, () =>
            {
                for (int phase = 0; phase < 6; phase++)
                {
                    w.Write(phase == 1 ? generator ? 2u : 1u : 0u);
                    if (phase != 1) continue;
                    if (generator) Pointer(helper ? (ushort)1502 : (ushort)500, () =>
                    {
                        w.Write((uint)histories.Length);
                        foreach (var row in histories) { w.Write(row.Id); foreach (int key in row.Keys) w.Write(key); }
                        w.Write(new byte[12]); w.Write(7); w.Write(0); w.Write(0); w.Write(2u);
                        if (!helper) w.Write(1); // generated cross section != current wall cross section
                    });
                    Pointer(596, () => { w.Write(new byte[16]); w.Write(8); w.Write(0); w.Write(flags); w.Write(2u); });
                }
                w.Write(new byte[16]); w.Write(2u); w.Write(new byte[20]); w.Write(0); w.Write((byte)0);
            });
            else if (f.Name == "m_cellList") Pointer(653, () =>
            {
                w.Write(helper ? 1u : 0u);
                if (helper) Pointer(4041, () => { w.Write(123456789); w.Write((byte)1); w.Write(0u); });
            });
            else Default(f);
        while (queue.TryDequeue(out var write)) write();
        w.Write(0u); var bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4); return bytes;
        void Pointer(ushort cls, Action body) { w.Write(uint.MaxValue); w.Write(cls); queue.Enqueue(body); }
        void Default(TfField f)
        {
            if (f.Mods >> 4 == 5) { w.Write(0u); return; }
            int count = f.Mods >> 4 == 1 ? f.Cnt!.Value : 1;
            for (int i = 0; i < count; i++) switch (f.Type)
            {
                case 1: case 2: case 3: w.Write((byte)0); break;
                case 4: case 5: case 6: w.Write(0); break;
                case 7: case 10: w.Write(0L); break;
                case 8: w.Write(0u); break;
                case 9: w.Write(new byte[16]); break;
                case 13: w.Write(new byte[(f.Cnt ?? 1) * 8]); break;
                case 14 when (f.Mods & 15) == 0 && f.Ref is not null and not 20:
                    foreach (var nested in SerializationFields(f.Ref.Value)!) Default(nested); break;
                case 14: w.Write(f.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(f.Name);
            }
        }
    }
}
