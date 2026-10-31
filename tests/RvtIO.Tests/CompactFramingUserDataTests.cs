using RvtIO.Compact;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The framing user data contribution to the parameter list of a family instance (group 181 of the FamilyInstance rules). The
/// facts are those of members of a Revit 2022 structural sample project.
/// </summary>
public class CompactFramingUserDataTests
{
    private sealed class Facts(Dictionary<string, int> values) : ICompactListFacts
    {
        public int? Get(string fact) => values.TryGetValue(fact, out int value) ? value : null;

        public IReadOnlyCollection<int>? StoredBuiltinIds => null;
    }

    private static Dictionary<string, int> Beam(int representation, int material, int coped, int attached = 0) => new()
    {
        ["fi:hasCurveDriver"] = 1, ["fi:structTypeFromSymbol"] = 1, ["fi:famSymbolicRepType"] = representation,
        ["fi:famStructMaterialType"] = material, ["fi:framingCoping"] = coped, ["fi:framingAttachment"] = attached,
    };

    private static (SortedSet<int> Ids, HashSet<int> Removed, bool Decided) Run(Dictionary<string, int> facts)
    {
        var ids = new SortedSet<int>();
        var removed = new HashSet<int>();
        return (ids, removed, CompactFramingUserData.Contribute(new Facts(facts), ids, removed));
    }

    [Fact]
    public void SteelBeamWithSymbolicRepresentation_ListsConnectionsAndTakesOutCamber()
    {
        var (ids, removed, decided) = Run(Beam(1, 3, 0));

        Assert.True(decided);
        Assert.Equal([-1152365, -1152364, -1152363, -1152362, -1152361, -1152358, -1152357, -1019016, -1019014, -1019012, -1019000,
            -1018802, -1018801, -1001503, -1001384, -1001381], ids);
        Assert.Equal(new HashSet<int> { -1001530, -1001529 }, removed);
    }

    [Fact]
    public void BeamWithoutSymbolicRepresentation_ListsNoConnections()
    {
        var (ids, _, decided) = Run(Beam(0, 3, 0));

        Assert.True(decided);
        Assert.DoesNotContain(-1018801, ids);
        Assert.DoesNotContain(-1018802, ids);
        Assert.DoesNotContain(-1001503, ids);
        Assert.Contains(-1001384, ids);
    }

    [Fact]
    public void MaterialType1_KeepsCamberAndCopedEndListsCopingDistance()
    {
        var (ids, removed, decided) = Run(Beam(1, 1, 1));

        Assert.True(decided);
        Assert.Contains(-1001559, ids);
        Assert.Empty(removed);
        Assert.DoesNotContain(-1001559, Run(Beam(1, 1, 0)).Ids);
    }

    [Fact]
    public void AttachedEndOrUnknownFact_LeavesTheContributionUndecided()
    {
        Assert.False(Run(Beam(1, 3, 0, attached: 1)).Decided);
        foreach (string fact in Beam(1, 3, 0).Keys)
        {
            var facts = Beam(1, 3, 0);
            facts.Remove(fact);
            var (ids, removed, decided) = Run(facts);
            Assert.False(decided);
            Assert.Empty(ids);
            Assert.Empty(removed);
        }
    }

    [Fact]
    public void MemberThatIsNotABeamWithACurveDriver_IsNotDecided()
    {
        var brace = Beam(1, 3, 0);
        brace["fi:structTypeFromSymbol"] = 2;
        Assert.False(Run(brace).Decided);
        var noDriver = Beam(1, 3, 0);
        noDriver["fi:hasCurveDriver"] = 0;
        Assert.False(Run(noDriver).Decided);
    }

    [Fact]
    public void ValidatedLabelsReplaceTheRevit2022Ones()
    {
        Assert.True(CompactLabels.TryGetLabels(-1001375, out string group, out string label));
        Assert.Equal("System Length", label);
        Assert.Equal("Dimensions", group);
        Assert.False(CompactLabelChanges.IsChanged(-1001375));
        // a changed caption with unconfirmed text stays out
        Assert.True(CompactLabelChanges.IsChanged(-1002503));
    }

    [Fact]
    public void AbsentIsStoredLineIsParsed()
    {
        var gate = CompactValueGate.Parse("C\tFamilyInstance\t-1001203\t5\nE\tFamilyInstance\t-1001203\t5\n");

        Assert.True(gate.AbsentIsStored("FamilyInstance", -1001203));
        Assert.False(gate.AbsentIsStored("Hub", -1001203));
        Assert.Equal(CompactValueGate.Source.Derived, gate.Of("FamilyInstance", -1001203));
    }
}
