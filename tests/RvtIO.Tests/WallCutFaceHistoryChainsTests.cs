using RvtIO.Parsing;
using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class WallCutFaceHistoryChainsTests
{
    [Theory]
    [InlineData(5)] [InlineData(15)] [InlineData(16)]
    public void ReferenceKindUsesKey1WithinTheSameGeneratorIncludingInactiveTarget(int kind)
    {
        var input = Input([Row(20, [12, 2, 3], false), Row(1, [kind, 20, 99], true)]);
        var chain = Assert.Single(Assert.Single(Read(input).Generators).Chains);
        Assert.Equal(new[] { 12, 2, 3 }, chain.TerminalKeys);
        var link = Assert.Single(chain.Links); Assert.Equal(20, link.ReferenceId); Assert.Equal("resolved", link.Outcome);
        Assert.Equal(1, chain.History.Id); Assert.Equal(1, chain.TableNode.Tag); // original current Face never changes
        Assert.Equal(20, input.Generators[0].Visits[chain.TerminalRowIndex].History.Id);
    }

    [Theory]
    [InlineData(int.MinValue)] [InlineData(0)] [InlineData(4)] [InlineData(6)] [InlineData(14)] [InlineData(17)] [InlineData(int.MaxValue)]
    public void OtherHistoryKindsDoNotFollowKey1(int kind)
    {
        var chain = Assert.Single(Assert.Single(Read(Input([Row(1, [kind, 20, 30], true)])).Generators).Chains);
        Assert.Empty(chain.Links); Assert.Equal(new[] { kind, 20, 30 }, chain.TerminalKeys);
    }

    [Fact]
    public void MultipleLinksKeepOriginalFaceAndAllSourceRowIndices()
    {
        var input = Input([Row(1, [5, 20, 7], true), Row(20, [15, 30, 8], false), Row(30, [12, 9, 10], false)]);
        var chain = Assert.Single(Assert.Single(Read(input).Generators).Chains);
        Assert.Equal(new[] { 20, 30 }, chain.Links.Select(l => l.ReferenceId));
        Assert.All(chain.Links, l => Assert.Equal("resolved", l.Outcome));
        Assert.Equal(new[] { 12, 9, 10 }, chain.TerminalKeys); Assert.Equal(1, chain.History.Id); Assert.Equal(1, chain.TableNode.Tag);
        Assert.Equal(input.Generators[0].Visits[chain.RowIndex].History, chain.History);
    }

    [Fact]
    public void MissingIdKeepsTheLastSuccessfullyResolvedKeys()
    {
        var input = Input([Row(1, [5, 20, 0], true), Row(20, [16, 1234, 9], false)]);
        var chain = Assert.Single(Assert.Single(Read(input).Generators).Chains);
        Assert.Equal(new[] { "resolved", "missing-id" }, chain.Links.Select(l => l.Outcome));
        Assert.Equal(new[] { 16, 1234, 9 }, chain.TerminalKeys); Assert.Null(chain.Links[1].ToRowIndex);
    }

    [Fact]
    public void SelfReferenceStopsBeforeLookingUpADifferentRowWithTheSameId()
    {
        var input = Input([Row(1, [1, 2, 3], false), Row(1, [5, 1, 9], true)]);
        var chain = Assert.Single(Assert.Single(Read(input).Generators).Chains);
        Assert.Equal("cycle", Assert.Single(chain.Links).Outcome); Assert.Equal(new[] { 5, 1, 9 }, chain.TerminalKeys);
    }

    [Fact]
    public void MultiRowCycleStopsBeforeReplacingCurrentKeysAgain()
    {
        var input = Input([Row(1, [5, 20, 0], true), Row(20, [15, 1, 8], false)]);
        var chain = Assert.Single(Assert.Single(Read(input).Generators).Chains);
        Assert.Equal(new[] { "resolved", "cycle" }, chain.Links.Select(l => l.Outcome));
        Assert.Equal(new[] { 15, 1, 8 }, chain.TerminalKeys);
    }

    [Fact]
    public void DuplicateIdsSelectFirstKeyOrderedRowNotActiveOrLastRow()
    {
        var input = Input([Row(20, [1, 2, 3], false), Row(20, [2, 3, 4], true), Row(1, [5, 20, 0], true)]);
        var chain = Assert.Single(Read(input).Generators).Chains.Single(c => c.History.Id == 1);
        Assert.Equal(new[] { 1, 2, 3 }, chain.TerminalKeys); Assert.Equal(0, chain.TerminalRowIndex);
    }

    [Theory]
    [InlineData(int.MinValue)] [InlineData(-1)] [InlineData(int.MaxValue)]
    public void ReferenceIdsAreSignedHistoryIdsNotGeometryTableBounds(int reference)
    {
        var input = Input([Row(reference, [12, 0, 0], false), Row(1, [5, reference, 0], true)]);
        var chain = Assert.Single(Assert.Single(Read(input).Generators).Chains);
        Assert.Equal("resolved", Assert.Single(chain.Links).Outcome); Assert.Equal(new[] { 12, 0, 0 }, chain.TerminalKeys);
    }

    [Fact]
    public void VisitedSetIsResetForEachActiveRowAndRowsAreNotDeduplicated()
    {
        var input = Input([Row(1, [5, 20, 0], true), Row(2, [5, 20, 1], true), Row(20, [12, 0, 0], false)]);
        var chains = Assert.Single(Read(input).Generators).Chains;
        Assert.Equal(2, chains.Count); Assert.All(chains, c => Assert.Equal("resolved", Assert.Single(c.Links).Outcome));
    }

    [Theory]
    [InlineData("null")] [InlineData("inactive-flag")] [InlineData("out-of-range")] [InlineData("not-face")]
    public void OnlyActiveRowsStartChainsButAllRowsRemainLookupTargets(string outcome)
    {
        var input = Input([Row(1, [5, 20, 0], true), Row(20, [12, 0, 0], false)]);
        var gen = input.Generators[0]; var visits = gen.Visits.Select(v => v.History.Id == 20 ? v with { Outcome = outcome } : v).ToArray();
        input = input with { Generators = [gen with { Visits = visits }] };
        Assert.Equal("resolved", Assert.Single(Assert.Single(Assert.Single(Read(input).Generators).Chains).Links).Outcome);
    }

    [Fact]
    public void EqualIdsInAnotherGeneratorDoNotResolveAMissingOwnId()
    {
        var input = Input([Row(1, [5, 20, 0], true)]); var other = Input([Row(20, [12, 0, 0], false)]).Generators[0];
        input = input with { Generators = [input.Generators[0], other with { StepQueueIndex = 99 }] };
        var value = Read(input); Assert.Equal("missing-id", Assert.Single(Assert.Single(value.Generators[0].Chains).Links).Outcome);
        Assert.Empty(value.Generators[1].Chains);
    }

    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void EmptyGeneratorAndAllMetadataArePreserved(int cls)
    {
        var input = Input([]) with { ClassId = (ushort)cls }; var value = Read(input);
        Assert.Equal(input.OwnerId, value.OwnerId); Assert.Equal(input.ClassId, value.ClassId); Assert.Equal(input.MaxId, value.MaxId);
        var gen = Assert.Single(value.Generators); Assert.Empty(gen.Chains);
        Assert.Equal(5, gen.PhaseIndex); Assert.Equal(7, gen.StepQueueIndex); Assert.Equal(77, gen.GeneratorId); Assert.Equal(-1, gen.Flags);
    }

    [Theory]
    [InlineData("rows")] [InlineData("keys")] [InlineData("duplicate-keys")] [InlineData("outcome")]
    public void InvalidRowContractFailsExplicitly(string mutation)
    {
        var input = Input([Row(1, [5, 20, 0], true)]); var gen = input.Generators[0]; var row = gen.Visits[0];
        gen = gen with { Visits = mutation switch
        {
            "rows" => [row with { RowIndex = 5 }],
            "keys" => [row with { History = row.History with { Keys = [1, 2] } }],
            "duplicate-keys" => [row, row with { RowIndex = 1 }],
            _ => [row with { Outcome = "unknown" }]
        } };
        Assert.Null(ResolveOwnWallDebugOffCutFaceHistoryChains(input with { Generators = [gen] }, out var failure));
        Assert.Equal("cut-history-rows:7", failure);
    }

    [Theory]
    [InlineData("tag")] [InlineData("flag")] [InlineData("kind")] [InlineData("archive")]
    public void AnInvalidActiveFaceIsNotFollowed(string mutation)
    {
        var input = Input([Row(1, [5, 20, 0], true)]); var gen = input.Generators[0]; var row = gen.Visits[0]; var node = row.TableNode!;
        node = mutation switch { "tag" => node with { Tag = 2 }, "flag" => node with { Flags = 0 },
            "kind" => node with { ClassId = 1243 }, _ => node with { Archive = "Unknown" } };
        input = input with { Generators = [gen with { Visits = [row with { TableNode = node }] }] };
        Assert.Null(ResolveOwnWallDebugOffCutFaceHistoryChains(input, out var failure)); Assert.Equal("cut-history-face:7:0", failure);
    }

    [Theory]
    [InlineData("owner")] [InlineData("class")] [InlineData("max")]
    public void UnsupportedContextFailsExplicitly(string mutation)
    {
        var input = Input([]); input = mutation switch { "owner" => input with { OwnerId = 0 }, "class" => input with { ClassId = 37 }, _ => input with { MaxId = -2 } };
        Assert.Null(ResolveOwnWallDebugOffCutFaceHistoryChains(input, out var failure)); Assert.Equal("cut-history-owner", failure);
    }

    private static OwnWallDebugOffCutFaceHistoryChains Read(OwnWallDebugOffActiveFaceHistories input)
    {
        var value = ResolveOwnWallDebugOffCutFaceHistoryChains(input, out var failure);
        Assert.Null(failure); return Assert.IsType<OwnWallDebugOffCutFaceHistoryChains>(value);
    }
    private static WallActiveFaceHistoryVisit Row(int id, int[] keys, bool active)
        => new(0, new(id, keys, 40, 56), active ? new("GRep", id + 100, 1589, id, 1u << 19, 400, 480) : null, active ? "active" : "null");
    private static OwnWallDebugOffActiveFaceHistories Input(WallActiveFaceHistoryVisit[] rows)
    {
        var ordered = rows.OrderBy(r => r.History.Keys[0]).ThenBy(r => r.History.Keys[1]).ThenBy(r => r.History.Keys[2])
            .Select((r, i) => r with { RowIndex = i, History = r.History with { Start = 40 + i * 16, End = 56 + i * 16 } }).ToArray();
        return new(100, 3524, 100, [new(5, 7, 500, 77, -1, ordered)]);
    }
}
