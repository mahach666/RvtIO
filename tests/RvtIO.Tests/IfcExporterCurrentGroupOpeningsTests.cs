using System.Reflection;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile puts the pair openings of a wall that belongs to a model group into the group, after the wall
/// (the group cache takes every product of the wall's product wrapper).</summary>
public class IfcExporterCurrentGroupOpeningsTests
{
    private static string[] GroupMembers(IfcProfile version, params int[] openingsOfWall)
    {
        var group = new Element { Id = 50, CategoryId = -2000095, Name = "Lift", SerializedGroupFamilyLabel = "Model group" };
        var wall = new Element { Id = 100, SerializedGroupId = 50 };
        var door = new Element { Id = 101, SerializedGroupId = 50 };
        var elements = new Dictionary<int, Element> { [50] = group, [100] = wall, [101] = door };
        var exporter = new IfcExporter { Profile = version };
        typeof(IfcExporter).GetField("_cutPairOpeningsByHostCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(exporter, new Dictionary<int, List<int>> { [100] = [.. openingsOfWall] });
        var writer = new StepWriter();
        typeof(IfcExporter).GetMethod("WriteModelGroups", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(exporter, [writer, 20, elements, new Dictionary<int, (int entityId, int placementId, string ifcType)>
            {
                [100] = (7, 8, "IFCWALLSTANDARDCASE"), [101] = (11, 12, "IFCDOOR"),
            }]);
        var relation = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).Single(entity => entity.Type == "IFCRELASSIGNSTOGROUP");
        string members = relation.Args[(relation.Args.IndexOf(",#20,$,$,(", StringComparison.Ordinal) + ",#20,$,$,(".Length)..];
        return members[..members.IndexOf(')', StringComparison.Ordinal)].Split(',');
    }

    [Fact]
    public void Current_OpeningsOfAGroupWall_FollowTheWallInTheGroup()
    {
        Assert.Equal(["#7", "#9", "#10", "#11"], GroupMembers(IfcProfile.Current, 9, 10));
    }

    [Fact]
    public void Current_WallWithoutPairOpenings_IsAloneInTheGroup()
    {
        Assert.Equal(["#7", "#11"], GroupMembers(IfcProfile.Current));
    }

    [Fact]
    public void Legacy_GroupKeepsOnlyTheProducts()
    {
        Assert.Equal(["#7", "#11"], GroupMembers(IfcProfile.Legacy, 9, 10));
    }
}
