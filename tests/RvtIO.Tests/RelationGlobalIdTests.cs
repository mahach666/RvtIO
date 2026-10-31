using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// Spatial relation and wall path connection GlobalIds.
/// Every expected value is the GlobalId of the Test_2022 export.
/// </summary>
public class RelationGlobalIdTests
{
    [Theory]
    [InlineData("3ZGD7y6S5209$mGLi_sPlj", "0M1SxRcfh5eV0GOZD2r2NA")] // project -> site
    [InlineData("3ZGD7y6S5209$mGLi_sPll", "04A45_PFvdL146cnhn8nRG")] // site -> building
    public void ContainmentCacheGlobalId_HashesTheContainerGlobalId(string container, string expected)
    {
        Assert.Equal(expected, IfcExporter.ContainmentCacheGlobalId(container));
        Assert.Null(IfcExporter.ContainmentCacheGlobalId(null));
    }

    [Fact]
    public void WallPathConnectionGlobalId_HashesProjectNameAndConnectionTypes()
    {
        Assert.Equal("0hPckbJUkIzea4vyRPUyLx", IfcExporter.WallPathConnectionGlobalId(
            "3ZGD7y6S5209$mGLi_sPlj", "3lLx0gNe59vvExhby0Bfew|38NblWsDL1I8DljLvn67bV", "ATSTART", "ATSTART"));
        Assert.NotEqual("0hPckbJUkIzea4vyRPUyLx", IfcExporter.WallPathConnectionGlobalId(
            "3ZGD7y6S5209$mGLi_sPlj", "3lLx0gNe59vvExhby0Bfew|38NblWsDL1I8DljLvn67bV", "ATPATH", "ATSTART"));
    }

    [Theory]
    // Storey "Level 1" (level 311): IfcRelContainedInSpatialStructure is sub-element 1.
    [InlineData("e3e052f9-0156-11d5-9301-0000863f27ad", 311u, IfcExporter.StoreyRelContainedInSpatialStructure, "3Zu5Bv0LOHrPC10066FoQQ")]
    // ProjectInformation 49504: building -> storeys IfcRelAggregates is sub-element 3.
    [InlineData("8764c510-57b7-44c3-bddf-266d86c26380", 49504u, IfcExporter.ProjectRelAggregatesBuildingStories, "27PCKGLxT4mxtV9cw6mgBW")]
    // Curtain wall 423099: its IfcRelAggregates is sub-element 1024.
    [InlineData("59371552-5800-43eb-9ba3-609565158fc5", 423099u, IfcExporter.CurtainWallRelAggregates, "1PDnLIM013wvkZP9Lb4$j_")]
    public void SubElementRelation_XorsItsIndexIntoTheOwnerExportId(
        string episode, uint id, int index, string expected)
    {
        var owner = new Element { Id = (int)id, SerializedExportId = ElementExportIds.ExportId(Guid.Parse(episode), id) };
        Assert.Equal(expected, IfcExporter.ResolveExactElementGlobalId(owner, new HashSet<Guid>(), index));
    }

    [Fact]
    public void SubElementRelation_OfACopiedElementTakesItsCurrentIdUnderCurrent()
    {
        // sample project A curtain wall 8641900 was copied from another model: its original id is 8637046. The expected GlobalId of the
        // wall derives from the original id, and that of its IfcRelAggregates (sub-element 1024) from the XOR of the current id.
        var wallExportId = IfcGuid.Parse("1Lx8QQfcz4zgx6MQ8H174q")!.Value;
        var episode = ElementExportIds.ExportId(wallExportId, 8637046u);
        var wall = new Element
        {
            Id = 8641900,
            SerializedExportId = ElementExportIds.ExportId(episode, 8637046u),
            SerializedHandleExportId = ElementExportIds.ExportId(episode, 8641900u),
        };
        Assert.Equal("1Lx8QQfcz4zgx6MQ8H174q", IfcExporter.ResolveExactElementGlobalId(wall, new HashSet<Guid>()));
        Assert.Equal("1Lx8QQfcz4zgx6NQ8H16Ok", IfcExporter.ResolveExactElementGlobalId(
            wall, new HashSet<Guid>(), IfcExporter.CurtainWallRelAggregates, handleBased: true));
        // The legacy profile keeps the original id.
        Assert.Equal("1Lx8QQfcz4zgx6NQ8H174q", IfcExporter.ResolveExactElementGlobalId(
            wall, new HashSet<Guid>(), IfcExporter.CurtainWallRelAggregates));
        // Without the current-id base the original one is used, and the product GlobalId never changes.
        wall.SerializedHandleExportId = null;
        Assert.Equal("1Lx8QQfcz4zgx6NQ8H174q", IfcExporter.ResolveExactElementGlobalId(
            wall, new HashSet<Guid>(), IfcExporter.CurtainWallRelAggregates, handleBased: true));
    }

    // Wall connections from control joins.

    private static TfBlockDeserializer.WallEndJoin Join(int end, int element, int joinedEnd) => new(end, element, joinedEnd, 0);

    [Fact]
    public void ExactWallConnections_FollowGetConnectedWalls()
    {
        var joins = new Dictionary<int, IReadOnlyList<TfBlockDeserializer.WallEndJoin>>
        {
            // 10's end meets 20's start (an L corner seen from both walls: one relation).
            [10] = [Join(0, 10, 0), Join(1, 20, 0), Join(1, 10, 1)],
            [20] = [Join(0, 20, 0), Join(0, 10, 1), Join(1, 20, 1), Join(5, 30, 0)],
            // 30's start meets mid end 5 of 20 (a T: 20 is joined on its path). The entry on
            // 20's mid end is not an end join and adds nothing.
            [30] = [Join(0, 20, 5), Join(0, 30, 0)],
            // 40 is joined to 50, whose joins are not read: no relation.
            [40] = [Join(0, 50, 1)],
        };
        var connections = IfcExporter.ResolveExactWallConnections(joins, new HashSet<int>());
        Assert.Equal(new[]
        {
            new IfcExporter.WallConnection(10, 20, "ATEND", "ATSTART"),
            new IfcExporter.WallConnection(20, 30, "ATPATH", "ATSTART"),
        }, connections);
    }

    [Fact]
    public void ExactWallConnections_KeepBothEntriesWhenTheWallsDisagree()
    {
        // WallConnectionData compares types too: two different entries for one pair are two
        // relations.
        var joins = new Dictionary<int, IReadOnlyList<TfBlockDeserializer.WallEndJoin>>
        {
            [10] = [Join(0, 20, 1)],
            [20] = [Join(0, 10, 0)],
        };
        Assert.Equal(new[]
        {
            new IfcExporter.WallConnection(10, 20, "ATSTART", "ATEND"),
            new IfcExporter.WallConnection(10, 20, "ATSTART", "ATSTART"),
        }.OrderBy(c => c.SecondType, StringComparer.Ordinal), IfcExporter.ResolveExactWallConnections(joins, new HashSet<int>()));
    }

    [Fact]
    public void ExactWallConnections_NeverReportACurtainWallAsTheJoinedWall()
    {
        // 10 and 30 are curtain walls: 10's end joined to the basic wall 20 is kept, the basic
        // wall's entry naming curtain wall 30 and the curtain-to-curtain entry are not.
        var joins = new Dictionary<int, IReadOnlyList<TfBlockDeserializer.WallEndJoin>>
        {
            [10] = [Join(0, 20, 5), Join(1, 30, 0)],
            [20] = [Join(1, 30, 1)],
            [30] = [Join(0, 10, 1)],
        };
        Assert.Equal(new[] { new IfcExporter.WallConnection(10, 20, "ATSTART", "ATPATH") },
            IfcExporter.ResolveExactWallConnections(joins, new HashSet<int> { 10, 30 }));
    }
}
