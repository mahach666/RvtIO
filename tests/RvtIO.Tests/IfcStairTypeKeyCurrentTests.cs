using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile caches the generic type of a stair by its type element and the IFC type of its shape
/// (CreateGenericTypeFromElement over getValidatedStairType). Stairs of one type element share their GlobalId and the GlobalId of their relation.</summary>
public class IfcStairTypeKeyCurrentTests
{
    private static readonly Guid TypeExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");

    [Fact]
    public void StairsOfOneTypeElement_SplitByShape()
    {
        var (text, _) = Write(currentProfile: true, ".STRAIGHT_RUN_STAIR.", ".NOTDEFINED.", ".STRAIGHT_RUN_STAIR.");
        Assert.Equal(2, Regex.Matches(text, "=IFCTYPEPRODUCT\\(").Count);
        Assert.Contains("(#1001,#1003),#", text);
        Assert.Contains("(#1002),#", text);
    }

    [Fact]
    public void StairsOfOneShape_ShareTheType()
    {
        var (text, sources) = Write(currentProfile: true, ".HALF_TURN_STAIR.", ".HALF_TURN_STAIR.", ".HALF_TURN_STAIR.");
        Assert.Single(Regex.Matches(text, "=IFCTYPEPRODUCT\\("));
        Assert.Contains("(#1001,#1002,#1003),#", text);
        Assert.Equal(IfcGuid.ToIfcGuid(TypeExportId), Assert.Single(sources).Value);
    }

    [Fact]
    public void SplitTypes_AllTakeTheExportIdOfTheTypeElement_AndTheirRelationsShareItsGlobalId()
    {
        var writer = new StepWriter();
        var sources = WriteTo(writer, currentProfile: true, ".STRAIGHT_RUN_STAIR.", ".NOTDEFINED.", ".HALF_TURN_STAIR.");
        Assert.Equal(3, sources.Count);
        Assert.All(sources.Values, guid => Assert.Equal(IfcGuid.ToIfcGuid(TypeExportId), guid));

        var apply = typeof(IfcExporter).GetMethod("ApplyTypeGlobalIds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(apply);
        Assert.Equal((3, 3), Assert.IsType<(int, int)>(apply.Invoke(null, [writer, sources])));
        var entities = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToArray();
        string relation = IfcGuid.FromMd5($"IfcRelDefinesByType:{IfcGuid.ToIfcGuid(TypeExportId)}");
        Assert.Equal(3, entities.Count(entity => entity.Type == "IFCTYPEPRODUCT" && entity.Args.StartsWith($"'{IfcGuid.ToIfcGuid(TypeExportId)}'")));
        Assert.Equal(3, entities.Count(entity => entity.Type == "IFCRELDEFINESBYTYPE" && entity.Args.StartsWith($"'{relation}'")));
    }

    [Fact]
    public void OlderRelease_HasNoStairTypes()
    {
        var (text, _) = Write(currentProfile: false, ".STRAIGHT_RUN_STAIR.", ".NOTDEFINED.", ".HALF_TURN_STAIR.");
        Assert.DoesNotContain("IFCTYPEPRODUCT(", text);
    }

    [Theory]
    [InlineData(".HALF_TURN_STAIR.", ".HALF_TURN_STAIR.", true)]
    [InlineData(".NOTDEFINED.", ".NOTDEFINED.", true)]
    [InlineData(".NOTDEFINED.", ".STRAIGHT_RUN_STAIR.", false)]
    [InlineData("$", "$", true)]
    [InlineData("$", ".NOTDEFINED.", false)]
    public void ShapeKey_DistinguishesEnumerationItems(string first, string second, bool equal) =>
        Assert.Equal(equal, IfcExporter.StairShapeKeyCurrent(first) == IfcExporter.StairShapeKeyCurrent(second));

    private static (string Text, Dictionary<int, string> Sources) Write(bool currentProfile, params string[] shapes)
    {
        var writer = new StepWriter();
        var sources = WriteTo(writer, currentProfile, shapes);
        using var text = new StringWriter();
        writer.WriteTo(text, "test.ifc", "IFC2X3", "test", "test");
        return (text.ToString(), sources);
    }

    private static Dictionary<int, string> WriteTo(StepWriter writer, bool currentProfile, params string[] shapes)
    {
        var elements = new Dictionary<int, Element>
        {
            [171672] = new() { Id = 171672, CategoryId = -2000120, Name = "Stair type", SerializedExportId = TypeExportId },
        };
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)>();
        for (int i = 0; i < shapes.Length; i++)
        {
            int id = i + 1;
            elements[id] = new Element { Id = id, CategoryId = -2000120, TypeId = 171672 };
            products.Add(id, (1000 + id, 0, "IFCSTAIR"));
            writer.AddWithId(1000 + id, "IFCSTAIR",
                $"'{IfcGuid.FromName($"stair:{id}")}',#900,'Stair:Type:{id}',$,'Stair:Type',#1,$,'{id}',{shapes[i]}");
        }
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByTypeCore",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<Dictionary<int, string>>(method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>(), currentProfile]));
    }
}
