using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The geometry rule for a beam in the current profile: sample project A types the 13 beams of one family type
/// with 13 IfcBeamTypes, one each, GlobalId = InstanceAsType sub-element of the beam.</summary>
public class IfcBeamTypeKeyCurrentTests
{
    private static readonly Guid FirstExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");
    private static readonly Guid SecondExportId = Guid.Parse("2a34752d-926d-4ab8-8425-c6d81fe02dab");

    [Fact]
    public void BeamsWithGeometryOfTheirOwn_GetATypeEach()
    {
        // sample project A: 14/70 and 18/105 faces and edges against 6/12 of the type element.
        var elements = Beams((14, 70), (18, 105), typeGeometry: new GeometryData { StoredNodeSummary = (6, 12) });
        var (text, sources) = Write(elements, currentProfile: true);
        Assert.Equal(2, Regex.Matches(text, "=IFCBEAMTYPE\\(").Count);
        Assert.Contains("(#1001),#", text);
        Assert.Contains("(#1002),#", text);
        Assert.Equal(
            new[] { IfcGuid.ToIfcGuid(ElementExportIds.SubElementId(FirstExportId, IfcExporter.InstanceAsTypeSubElementIndex)),
                    IfcGuid.ToIfcGuid(ElementExportIds.SubElementId(SecondExportId, IfcExporter.InstanceAsTypeSubElementIndex)) }.Order(),
            sources.Values.Order());
    }

    [Fact]
    public void BeamWhoseSummaryDiffersFromTheTypeElement_GetsATypeOfItsOwn()
    {
        var elements = Beams((6, 12), (8, 18), typeGeometry: new GeometryData { StoredNodeSummary = (6, 12) });
        string text = Write(elements, currentProfile: true).Text;
        // Beam 1 has the summary of the type element and keeps the shared type; beam 2 gets one of its own.
        Assert.Equal(2, Regex.Matches(text, "=IFCBEAMTYPE\\(").Count);
        Assert.Contains("(#1001),#", text);
        Assert.Contains("(#1002),#", text);
    }

    [Theory]
    [InlineData(0, 0)]   // the geometry holds only a GInstance
    [InlineData(6, 12)]  // the same summary as the type element
    public void BeamSharingTheSummaryOfTheTypeElement_KeepsTheSharedType(int faces, int edges)
    {
        var elements = Beams((faces, edges), (faces, edges), typeGeometry: new GeometryData { StoredNodeSummary = (6, 12) });
        string text = Write(elements, currentProfile: true).Text;
        Assert.Single(Regex.Matches(text, "=IFCBEAMTYPE\\("));
        Assert.Contains("(#1001,#1002),#", text);
    }

    [Fact]
    public void TypeElementWithoutGeometry_KeepsTheSharedType()
    {
        var elements = Beams((6, 12), (6, 12), typeGeometry: null);
        Assert.Single(Regex.Matches(Write(elements, currentProfile: true).Text, "=IFCBEAMTYPE\\("));
    }

    [Fact]
    public void TypeElementWithAnUnknownSummary_KeepsTheSharedType()
    {
        var elements = Beams((6, 12), (6, 12), typeGeometry: new GeometryData());
        Assert.Single(Regex.Matches(Write(elements, currentProfile: true).Text, "=IFCBEAMTYPE\\("));
    }

    [Fact]
    public void BeamWithoutGeometry_KeepsTheSharedType()
    {
        var elements = Beams((6, 12), (6, 12), typeGeometry: null);
        elements[1].Geometry = null;
        elements[2].Geometry = null;
        Assert.Single(Regex.Matches(Write(elements, currentProfile: true).Text, "=IFCBEAMTYPE\\("));
    }

    [Fact]
    public void OlderRelease_KeepsOneTypeForTheBeams()
    {
        var elements = Beams((6, 12), (6, 12), typeGeometry: null);
        var (text, sources) = Write(elements, currentProfile: false);
        Assert.Single(Regex.Matches(text, "=IFCBEAMTYPE\\("));
        Assert.Contains("(#1001,#1002),#", text);
        Assert.Empty(sources);
    }

    private static Dictionary<int, Element> Beams((int Faces, int Edges) first, (int Faces, int Edges) second, GeometryData? typeGeometry)
    {
        Element Beam(int id, Guid exportId, (int Faces, int Edges) summary) => new()
        {
            Id = id, CategoryId = -2001320, SerializedExportId = exportId,
            Parameters = [new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = 500,
            }],
            Geometry = new GeometryData { StoredNodeSummary = summary },
        };
        return new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Beam type", CategoryId = -2001320, Geometry = typeGeometry },
            [1] = Beam(1, FirstExportId, first),
            [2] = Beam(2, SecondExportId, second),
        };
    }

    private static (string Text, Dictionary<int, string> Sources) Write(Dictionary<int, Element> elements, bool currentProfile)
    {
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)>();
        foreach (int id in new[] { 1, 2 })
            products.Add(id, (1000 + id, 0, "IFCBEAM"));
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByTypeCore",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var sources = Assert.IsType<Dictionary<int, string>>(method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>(), currentProfile]));
        using var text = new StringWriter();
        writer.WriteTo(text, "test.ifc", "IFC2X3", "test", "test");
        return (text.ToString(), sources);
    }
}
