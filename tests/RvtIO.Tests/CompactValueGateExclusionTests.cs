using RvtIO.Compact;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The exclusion lines ("X class id") of the value gate table: a pair whose written value was wrong in every observation
/// of a file the table was not learned from.</summary>
public class CompactValueGateExclusionTests
{
    private const string Table = "# a stored id, a derived id, a derived pair and an exclusion of the stored id for one class\n"
        + "S\t-1006326\t14085\nD\t-1005001\t12\nC\tFloor\t-1005002\t9\nX\tTextNoteAttributes\t-1006326\t260\n";

    [Fact]
    public void ExcludedPair_WritesNothing_TheSameIdOfAnotherClassKeepsItsSource()
    {
        var gate = CompactValueGate.Parse(Table);
        Assert.Equal(CompactValueGate.Source.None, gate.Of("TextNoteAttributes", -1006326));
        Assert.Equal(CompactValueGate.Source.Stored, gate.Of("TagNoteAttributes", -1006326));
        Assert.Equal(1, gate.ExcludedPairCount);
    }

    [Fact]
    public void OtherLines_AreNotAffected()
    {
        var gate = CompactValueGate.Parse(Table);
        Assert.Equal(CompactValueGate.Source.Derived, gate.Of("Wall", -1005001));
        Assert.Equal(CompactValueGate.Source.Derived, gate.Of("Floor", -1005002));
        Assert.Equal(CompactValueGate.Source.None, gate.Of("Wall", -1005002));
    }

    [Fact]
    public void ExclusionsApplyToTheRevit2022LayoutOnly()
    {
        Assert.Equal(0, CompactValueGate.Parse(Table, exclusions: false).ExcludedPairCount);
        Assert.Equal(CompactValueGate.Source.Stored, CompactValueGate.Parse(Table, exclusions: false).Of("TextNoteAttributes", -1006326));
        Assert.Equal(0, CompactValueGate.ForLayout(wide: true).ExcludedPairCount);
        Assert.True(CompactValueGate.ForLayout(wide: false).ExcludedPairCount > 0);
        Assert.Equal(CompactValueGate.Source.None, CompactValueGate.ForLayout(wide: false).Of("TextNoteAttributes", -1006326));
        Assert.Equal(CompactValueGate.Source.Stored, CompactValueGate.ForLayout(wide: true).Of("TextNoteAttributes", -1006326));
    }

    [Theory]
    [InlineData("X\tFloor\n")]
    [InlineData("X\tFloor\tnot-a-number\n")]
    public void MalformedExclusion_IsRefused(string text) => Assert.Throws<InvalidDataException>(() => CompactValueGate.Parse(text));
}
