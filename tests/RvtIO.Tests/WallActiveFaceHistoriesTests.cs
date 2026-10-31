using RvtIO.Parsing;
using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class WallActiveFaceHistoriesTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void HistoryIdSelectsTheCurrentQualifiedTableFaceNotTheGeneratorColumn(int cls)
    {
        var (input, table) = Inputs((ushort)cls);
        var value = Read(input, table);
        var generator = Assert.Single(value.Generators);
        Assert.Equal(77, generator.GeneratorId);
        Assert.Equal(999, table.Slots[1].Persisted.GeneratorId);
        var visit = Assert.Single(generator.Visits);
        Assert.Equal("active", visit.Outcome); Assert.Equal(table.Slots[1].Node, visit.TableNode);
        Assert.Equal(40, visit.History.Start); Assert.Equal(56, visit.History.End);
    }

    [Theory]
    [InlineData(0)] [InlineData(0x100000)] [InlineData(-1)]
    public void StepFlagsDoNotFilterActiveHistoryRows(int flags)
    {
        var (input, table) = Inputs(flags: flags);
        var generator = Assert.Single(Read(input, table).Generators);
        Assert.Equal(flags, generator.Flags); Assert.Equal("active", Assert.Single(generator.Visits).Outcome);
    }

    [Theory]
    [InlineData(0u, "inactive-flag")] [InlineData(1u, "inactive-flag")]
    [InlineData(1u << 19, "active")] [InlineData(uint.MaxValue, "active")]
    public void OnlyTheCurrentFacesBit19IsTheActiveFlag(uint flags, string expected)
    {
        var (input, table) = Inputs(nodeFlags: flags);
        Assert.Equal(expected, Assert.Single(Assert.Single(Read(input, table).Generators).Visits).Outcome);
    }

    [Theory]
    [InlineData(1736)] [InlineData(1974)] [InlineData(1243)]
    public void ACurveOrEdgeDoesNotBecomeAFaceThroughItsActiveFlag(int kind)
    {
        var (input, table) = Inputs(kind: (ushort)kind);
        var visit = Assert.Single(Assert.Single(Read(input, table).Generators).Visits);
        Assert.NotNull(visit.TableNode); Assert.Equal("not-face", visit.Outcome);
    }

    [Theory]
    [InlineData(int.MinValue)] [InlineData(-1)] [InlineData(2)] [InlineData(int.MaxValue)]
    public void SignedHistoryIdsOutsideNativeBoundsAreRetainedButNotResolved(int id)
    {
        var (input, table) = Inputs(rows: [new(id, [1, -1, -1], 40, 56)]);
        var visit = Assert.Single(Assert.Single(Read(input, table).Generators).Visits);
        Assert.Equal(id, visit.History.Id); Assert.Null(visit.TableNode); Assert.Equal("out-of-range", visit.Outcome);
    }

    [Fact]
    public void NullCurrentSlotIsNotRecoveredFromOtherSerializedFaces()
    {
        var (input, table) = Inputs();
        table = table with { Slots = [table.Slots[0], table.Slots[1] with { Node = null }] };
        Assert.Equal("null", Assert.Single(Assert.Single(Read(input, table).Generators).Visits).Outcome);
    }

    [Theory]
    [InlineData("Wall")] [InlineData("GRep")]
    public void ArchiveIdentityIsRetainedAndEqualQueueNumbersAreNotAliased(string archive)
    {
        var (input, table) = Inputs(archive: archive);
        Assert.Equal(archive, Assert.Single(Assert.Single(Read(input, table).Generators).Visits).TableNode!.Archive);
    }

    [Fact]
    public void RepeatedFaceIdsAreNotDeduplicatedWhenHistoryKeysDiffer()
    {
        WallHistoryEntry[] rows = [new(1, [-1, 2, 3], 40, 56), new(1, [0, -1, -1], 56, 72)];
        var (input, table) = Inputs(rows: rows);
        var visits = Assert.Single(Read(input, table).Generators).Visits;
        Assert.Equal(2, visits.Count); Assert.Equal(new[] { 0, 1 }, visits.Select(v => v.RowIndex));
        Assert.All(visits, v => Assert.Equal("active", v.Outcome)); Assert.Equal(visits[0].TableNode, visits[1].TableNode);
        Assert.Equal(rows, visits.Select(v => v.History));
    }

    [Fact]
    public void AllSixPhasesRetainTheirOwnOrderedGeneratorsIncludingEmptyHistories()
    {
        var (input, table) = Inputs(rows: []); var h = input.Histories.Histories;
        var first = h.Steps[0];
        var steps = Enumerable.Range(0, 6).Select(i => first with { QueueIndex = i + 1, Id = i + 10 }).ToArray();
        var phases = steps.Select((s, i) => new WallPhaseArray(i.ToString(), 1, 0, 0,
            [new(uint.MaxValue, s.ClassId, s.QueueIndex, 0, 0)])).ToArray();
        input = Replace(input, h with { Steps = steps, List = h.List with { Phases = phases } });
        var value = Read(input, table);
        Assert.Equal(Enumerable.Range(0, 6), value.Generators.Select(g => g.PhaseIndex));
        Assert.Equal(Enumerable.Range(10, 6), value.Generators.Select(g => g.GeneratorId));
        Assert.All(value.Generators, g => Assert.Empty(g.Visits));
    }

    [Fact]
    public void ZeroPhysicalBodiesDoesNotImplicitlySkipAnOwnActiveFaceHistory()
    {
        var (input, table) = Inputs();
        Assert.Equal("active", Assert.Single(Assert.Single(Read(input, table with { GeometryCount = 0 }).Generators).Visits).Outcome);
    }

    [Theory]
    [InlineData("unsorted")] [InlineData("duplicate")] [InlineData("null-count")]
    [InlineData("count")] [InlineData("keys")]
    public void UnconfirmedHistoryNormalizationFailsExplicitly(string mutation)
    {
        var (input, table) = Inputs(); var h = input.Histories.Histories; var s = h.Steps[0]; var t = s.Tables[0];
        t = mutation switch
        {
            "unsorted" => t with { Count = 2, Entries = [new(1, [1, 0, 0], 0, 16), new(1, [-1, 0, 0], 16, 32)] },
            "duplicate" => t with { Count = 2, Entries = [t.Entries[0], t.Entries[0] with { Id = 0 }] },
            "null-count" => t with { Count = uint.MaxValue },
            "count" => t with { Count = 2 },
            _ => t with { Entries = [t.Entries[0] with { Keys = [1, 2] }] }
        };
        input = Replace(input, h with { Steps = [s with { Tables = [t] }] });
        Assert.Null(SelectOwnWallDebugOffActiveFaceHistories(input, table, out var failure));
        Assert.Equal("active-history-order:1", failure);
    }

    [Theory]
    [InlineData("owner")] [InlineData("class")] [InlineData("bound")] [InlineData("slot")]
    [InlineData("tag")] [InlineData("root")] [InlineData("incomplete")] [InlineData("phases")]
    public void IncompatibleOwnTableOrHistoryContextIsNotUsed(string mutation)
    {
        var (input, table) = Inputs(); var h = input.Histories.Histories;
        switch (mutation)
        {
            case "owner": table = table with { OwnerId = 101 }; break;
            case "class": table = table with { ClassId = 361 }; break;
            case "bound": table = table with { MaxId = 2 }; break;
            case "slot": table = table with { Slots = [table.Slots[0] with { Persisted = table.Slots[0].Persisted with { Id = 9 } }, table.Slots[1]] }; break;
            case "tag": table = table with { Slots = [table.Slots[0], table.Slots[1] with { Node = table.Slots[1].Node! with { Tag = 0 } }] }; break;
            case "root": input = Replace(input, h with { List = h.List with { OwnerReference = 3 } }); break;
            case "incomplete": input = Replace(input, h with { Complete = false }); break;
            case "phases": input = Replace(input, h with { List = h.List with { Phases = [] } }); break;
        }
        Assert.Null(SelectOwnWallDebugOffActiveFaceHistories(input, table, out var failure)); Assert.Equal("active-history-owner", failure);
    }

    [Theory]
    [InlineData("kind")] [InlineData("archive")]
    public void UnboundRuntimeKindOrArchiveIsNotGuessed(string mutation)
    {
        var (input, table) = Inputs(kind: mutation == "kind" ? (ushort)1590 : (ushort)1589,
            archive: mutation == "archive" ? "Unknown" : "GRep");
        Assert.Null(SelectOwnWallDebugOffActiveFaceHistories(input, table, out var failure));
        Assert.Equal("active-history-node:1:0:1", failure);
    }

    [Theory]
    [InlineData("reference")] [InlineData("class")]
    public void PhaseStepMustBeItsOwnResolvedClass(string mutation)
    {
        var (input, table) = Inputs(); var h = input.Histories.Histories; var s = h.Steps[0];
        s = mutation == "reference" ? s with { OwnerReference = 3 } : s with { ClassId = 596 };
        input = Replace(input, h with { Steps = [s] });
        Assert.Null(SelectOwnWallDebugOffActiveFaceHistories(input, table, out var failure)); Assert.Equal("active-history-step:0:1", failure);
    }

    [Fact]
    public void AnUnboundStepDoesNotAssumeTheSameVirtualTableGetter()
    {
        var (input, table) = Inputs(); var h = input.Histories.Histories;
        var phases = h.List.Phases.ToArray(); var p = phases[0].Members[0] with { ClassId = 96 };
        phases[0] = phases[0] with { Members = [p] };
        input = Replace(input, h with { Steps = [h.Steps[0] with { ClassId = 96 }], List = h.List with { Phases = phases } });
        Assert.Null(SelectOwnWallDebugOffActiveFaceHistories(input, table, out var failure)); Assert.Equal("active-history-step:0:1", failure);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrDuplicatedFaceHistoryTableIsNotSubstituted(bool duplicate)
    {
        var (input, table) = Inputs(); var h = input.Histories.Histories; var s = h.Steps[0];
        input = Replace(input, h with { Steps = [s with { Tables = duplicate ? [s.Tables[0], s.Tables[0]] : [] }] });
        Assert.Null(SelectOwnWallDebugOffActiveFaceHistories(input, table, out var failure)); Assert.Equal("active-history-step:0:1", failure);
    }

    [Fact]
    public void SignedKeyComparisonHasNoOverflowAtIntExtremes()
    {
        var (input, table) = Inputs(rows: [new(1, [int.MinValue, 0, 0], 40, 56), new(1, [int.MaxValue, 0, 0], 56, 72)]);
        Assert.Equal(2, Assert.Single(Read(input, table).Generators).Visits.Count);
    }

    [Fact]
    public void NativeMinusOneMaximumAllowsAnEmptyTableAndRetainsInvalidRows()
    {
        var (input, table) = Inputs(); table = table with { MaxId = -1, Slots = [] };
        Assert.Equal("out-of-range", Assert.Single(Assert.Single(Read(input, table).Generators).Visits).Outcome);
    }

    private static OwnWallDebugOffActiveFaceHistories Read(OwnWallCompleteInputs input, OwnWallDebugOffClearedTable table)
    {
        var value = SelectOwnWallDebugOffActiveFaceHistories(input, table, out var failure);
        Assert.Null(failure); return Assert.IsType<OwnWallDebugOffActiveFaceHistories>(value);
    }
    private static OwnWallCompleteInputs Replace(OwnWallCompleteInputs input, OwnWallGeometryHistories history)
        => input with { Histories = input.Histories with { Histories = history } };
    private static (OwnWallCompleteInputs, OwnWallDebugOffClearedTable) Inputs(ushort cls = 3524, int flags = 0,
        uint nodeFlags = 1u << 19, ushort kind = 1589, string archive = "GRep", WallHistoryEntry[]? rows = null)
    {
        rows ??= [new(1, [1, -1, -1], 40, 56)];
        var faces = new WallHistoryTable("m_faceHistTable", (uint)rows.Length, 36, 40 + rows.Length * 16, rows);
        var step = new WallGeometryStepPrefix(1, 500, uint.MaxValue, 36, 120, [faces], 77, 0, flags, 2);
        var phases = Enumerable.Range(0, 6).Select(i => new WallPhaseArray(i.ToString(), i == 0 ? 1u : 0u, 0, 0,
            i == 0 ? [new(uint.MaxValue, step.ClassId, step.QueueIndex, 0, 0)] : [])).ToArray();
        var list = new OwnWallGeometryPhases(100, cls, 14, new(uint.MaxValue, 2086, 0, 0, 0), 14, 36,
            [], phases, [], 2, [], 0, 0);
        var history = new OwnWallGeometryHistories(list, [], [step], 1, 120, true, null,
            [], [], [], [], new(0, null, null, 0, 0), []);
        return (new(new(history, [], [], []), [], [], 120), new(100, cls, 1, 1, [],
            [new(new(0, -2, "Default", null, 0, null, null), null),
             new(new(1, 999, "Default", null, 1, null, null), new(archive, 123, kind, 1, nodeFlags, 400, 480))]));
    }
}
