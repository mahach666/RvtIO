using RvtIO.Compact;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The compact JSON universe is the ElemTable rows, with the same row rule IfcExporter applies to its products
/// (see IfcExporterGhostFilterTests).</summary>
public class CompactUniverseTests
{
    private static Element E(int id, bool discovered = false, bool blockOnly = false)
        => new() { Id = id, IsDiscovered = discovered, IsBlockOnly = blockOnly };

    private static int[] Ids(RevitFileInfo info) => CompactUniverse.Select(info).Select(element => element.Id).ToArray();

    [Fact]
    public void RecordOutsideAnExactElemTable_IsNotARow()
    {
        var info = new RevitFileInfo { ElemTableIsExact = true, Elements = { E(1), E(2, discovered: true), E(3) } };
        Assert.Equal(new[] { 1, 3 }, Ids(info));
    }

    [Fact]
    public void RecordOutsideAnInexactElemTable_IsKept()
    {
        var info = new RevitFileInfo { ElemTableIsExact = false, Elements = { E(1), E(2, discovered: true), E(3) } };
        Assert.Equal(new[] { 1, 2, 3 }, Ids(info));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BlockOnlyRecord_IsNeverARow(bool tableIsExact)
    {
        var info = new RevitFileInfo { ElemTableIsExact = tableIsExact, Elements = { E(1), E(2, blockOnly: true), E(3, discovered: true, blockOnly: true) } };
        Assert.Equal(new[] { 1 }, Ids(info));
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, false)]
    public void IsTableRow_CoversEveryCombination(bool discovered, bool blockOnly, bool exactTable, bool expected)
        => Assert.Equal(expected, CompactUniverse.IsTableRow(E(1, discovered, blockOnly), exactTable));

    [Fact]
    public void Rows_AreOrderedByAscendingSignedElementId()
    {
        var info = new RevitFileInfo { Elements = { E(300), E(5), E(-7), E(12), E(0), E(2_000_000_000) } };
        Assert.Equal(new[] { -7, 0, 5, 12, 300, 2_000_000_000 }, Ids(info));
    }

    [Fact]
    public void ElementsWithTheSameId_KeepTheirRelativeOrder()
    {
        var first = E(9);
        var second = E(9);
        var third = E(9);
        var info = new RevitFileInfo { Elements = { E(10), third, first, second, E(1) } };
        var rows = CompactUniverse.Select(info);
        Assert.Same(third, rows[1]);
        Assert.Same(first, rows[2]);
        Assert.Same(second, rows[3]);
    }

    [Fact]
    public void SelectDoesNotChangeTheElementList()
    {
        var info = new RevitFileInfo { ElemTableIsExact = true, Elements = { E(3), E(1, discovered: true), E(2) } };
        CompactUniverse.Select(info);
        Assert.Equal(new[] { 3, 1, 2 }, info.Elements.Select(element => element.Id));
    }
}
