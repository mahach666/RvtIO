using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>A placed instance on a project Level that is not flagged "building story" is still a placed product. Only Levels owned by a family, an unplaced instance or a DB view are family-local.</summary>
public class IfcExporterProjectLevelTests
{
    private static Element Level(int family = -1, int unplaced = -1, int dbView = -1, bool native = true) => new()
    {
        Id = 1,
        CategoryId = -2000240,
        IsNativeLevel = native,
        SerializedFamilyId = family,
        SerializedUnplacedOwnerId = unplaced,
        SerializedOwnerDbViewId = dbView,
    };

    [Fact]
    public void ProjectLevelWithoutBuildingStoryFlag_IsProjectLevel() =>
        Assert.True(IfcExporter.IsProjectLevel(Level()));

    [Theory]
    [InlineData(5, -1, -1)]
    [InlineData(-1, 5, -1)]
    [InlineData(-1, -1, 5)]
    public void FamilyLocalLevel_IsNotProjectLevel(int family, int unplaced, int dbView) =>
        Assert.False(IfcExporter.IsProjectLevel(Level(family, unplaced, dbView)));

    [Fact]
    public void NonNativeLevel_IsNotProjectLevel() =>
        Assert.False(IfcExporter.IsProjectLevel(Level(native: false)));
}
