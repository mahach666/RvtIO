using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcMappedSymbolTypeTests
{
    [Theory]
    [InlineData("IFCMEMBER", "IFCMEMBERTYPE", false)]
    [InlineData("IFCMEMBER", "IFCMEMBERTYPE", true)]
    [InlineData("IFCPLATE", "IFCPLATETYPE", false)]
    [InlineData("IFCPLATE", "IFCPLATETYPE", true)]
    [InlineData("IFCCOLUMN", "IFCCOLUMNTYPE", false)]
    [InlineData("IFCCOLUMN", "IFCCOLUMNTYPE", true)]
    [InlineData("IFCBUILDINGELEMENTPROXY", "IFCBUILDINGELEMENTPROXYTYPE", false)]
    [InlineData("IFCBUILDINGELEMENTPROXY", "IFCBUILDINGELEMENTPROXYTYPE", true)]
    [InlineData("IFCFURNISHINGELEMENT", "IFCFURNITURETYPE", false)]
    [InlineData("IFCFURNISHINGELEMENT", "IFCFURNITURETYPE", true)]
    [InlineData("IFCFLOWTERMINAL", "IFCDISTRIBUTIONELEMENTTYPE", false)]
    [InlineData("IFCFLOWTERMINAL", "IFCDISTRIBUTIONELEMENTTYPE", true)]
    [InlineData("IFCFLOWSEGMENT", "IFCDISTRIBUTIONELEMENTTYPE", false)]
    [InlineData("IFCFLOWSEGMENT", "IFCDISTRIBUTIONELEMENTTYPE", true)]
    [InlineData("IFCSLAB", "IFCSLABTYPE", false)]
    [InlineData("IFCSLAB", "IFCSLABTYPE", true)]
    [InlineData("IFCSTAIRFLIGHT", "IFCSTAIRFLIGHTTYPE", false)]
    [InlineData("IFCSTAIRFLIGHT", "IFCSTAIRFLIGHTTYPE", true)]
    public void DistinctGeometrySymbols_SplitTypesWithoutChangingDisplayIdentity(string product, string type, bool reverse)
    {
        var elements = Elements(601, 602);
        string text = Write(elements, product, reverse);
        var types = Lines(text, type);
        Assert.Equal(2, types.Length);
        if (product == "IFCPLATE")
            // A curtain panel type is tagged by its first panel.
            Assert.Equal(new[] { "'1'", "'2'" }, types.Select(line => line.Split(',')[7]).OrderBy(tag => tag).ToArray());
        else
            Assert.All(types, line => Assert.Contains("'Own family:Own type',$,$,$,$,'500',", line));
        Assert.Contains($"(#1001),#{Id(types[reverse ? 1 : 0])});", text);
        Assert.Contains($"(#1002),#{Id(types[reverse ? 0 : 1])});", text);
        Assert.Equal(2, types.Select(line => line.Split(',')[0]).Distinct().Count());
    }

    [Theory]
    [InlineData("IFCMEMBER", "IFCMEMBERTYPE")]
    [InlineData("IFCPLATE", "IFCPLATETYPE")]
    [InlineData("IFCCOLUMN", "IFCCOLUMNTYPE")]
    [InlineData("IFCFURNISHINGELEMENT", "IFCFURNITURETYPE")]
    [InlineData("IFCFLOWTERMINAL", "IFCDISTRIBUTIONELEMENTTYPE")]
    [InlineData("IFCFLOWSEGMENT", "IFCDISTRIBUTIONELEMENTTYPE")]
    [InlineData("IFCSLAB", "IFCSLABTYPE")]
    [InlineData("IFCSTAIRFLIGHT", "IFCSTAIRFLIGHTTYPE")]
    public void SharedGeometrySymbol_GroupsOccurrences(string product, string type)
    {
        string text = Write(Elements(601, 601), product);
        int id = Id(Assert.Single(Lines(text, type)));
        Assert.Contains($"(#1001,#1002),#{id});", text);
    }

    [Fact]
    public void CurrentBeam_IsKeyedByTheSymbolOfItsInstance()
    {
        var elements = Elements(601, 602);
        string shared = Write(Elements(601, 601), "IFCBEAM", currentProfile: true);
        Assert.Single(Lines(shared, "IFCBEAMTYPE"));
        string split = Write(elements, "IFCBEAM", currentProfile: true);
        var types = Lines(split, "IFCBEAMTYPE");
        Assert.Equal(2, types.Length);
        Assert.Contains($"(#1001),#{Id(types[0])});", split);
        Assert.Contains($"(#1002),#{Id(types[1])});", split);
    }

    [Theory]
    [InlineData("IFCWALL", "IFCWALLTYPE")]
    [InlineData("IFCCURTAINWALL", "IFCCURTAINWALLTYPE")]
    [InlineData("IFCBEAM", "IFCBEAMTYPE")]
    [InlineData("IFCCOVERING", "IFCCOVERINGTYPE")]
    [InlineData("IFCRAILING", "IFCRAILINGTYPE")]
    [InlineData("IFCDOOR", "IFCDOORSTYLE")]
    [InlineData("IFCWINDOW", "IFCWINDOWSTYLE")]
    public void OtherExportPaths_KeepCanonicalTypeGrouping(string product, string type)
    {
        string text = Write(Elements(601, 602), product);
        int id = Id(Assert.Single(Lines(text, type)));
        Assert.Contains($"(#1001,#1002),#{id});", text);
        Assert.Contains($"'{IfcGuid.FromName("type:500")}'", text);
    }

    [Theory]
    [InlineData(0)] // multiple top-level symbols
    [InlineData(1)] // unresolved symbol
    [InlineData(2)] // self-reference
    [InlineData(3)] // lookup record has another identity
    public void AmbiguousMappedSymbol_DoesNotCreateATypeRelation(int scenario)
    {
        var elements = Elements(601, 601);
        switch (scenario)
        {
            case 0: elements[1].Geometry!.ExactMappedInstances!.Add(new GeoInstance { SymbolId = 602 }); break;
            case 1: elements.Remove(601); break;
            case 2: elements[1].Geometry!.ExactMappedInstances![0].SymbolId = 1; break;
            case 3: elements[601].Id = 999; break;
        }
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY");
        Assert.DoesNotContain("(#1001)", text);
        Assert.DoesNotContain("(#1001,#1002)", text);
        if (scenario is 1 or 3)
            Assert.Empty(Lines(text, "IFCBUILDINGELEMENTPROXYTYPE"));
        else
            Assert.Contains($"(#1002),#{Id(Assert.Single(Lines(text, "IFCBUILDINGELEMENTPROXYTYPE")))});", text);
    }

    [Theory]
    [InlineData(6, 39, 6, 12, 2)] // extra edges: the instance has geometry of its own
    [InlineData(7, 12, 6, 12, 2)] // extra face
    [InlineData(6, 12, 6, 12, 1)] // same summary as the symbol
    [InlineData(0, 0, 6, 12, 1)] // the geometry holds only a GInstance
    public void ProxyWithGeometryOfItsOwn_GetsATypeOfItsOwn(
        int faces, int edges, int symbolFaces, int symbolEdges, int expectedTypes)
    {
        var elements = Elements(601, 601);
        elements[1].Geometry!.StoredNodeSummary = (faces, edges);
        elements[2].Geometry!.StoredNodeSummary = (symbolFaces, symbolEdges);
        elements[601].Geometry = new GeometryData { StoredNodeSummary = (symbolFaces, symbolEdges) };
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY");
        var types = Lines(text, "IFCBUILDINGELEMENTPROXYTYPE");
        Assert.Equal(expectedTypes, types.Length);
        if (expectedTypes == 2)
        {
            Assert.Contains($"(#1001),#{Id(types[0])});", text);
            Assert.Contains($"(#1002),#{Id(types[1])});", text);
        }
    }

    [Fact]
    public void ProxyWithUnknownSymbolSummary_KeepsTheSharedType()
    {
        var elements = Elements(601, 601);
        elements[1].Geometry!.StoredNodeSummary = (6, 39);
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY");
        Assert.Single(Lines(text, "IFCBUILDINGELEMENTPROXYTYPE"));
    }

    [Fact]
    public void OtherClassesIgnoreTheInstanceGeometrySummary()
    {
        var elements = Elements(601, 601);
        elements[1].Geometry!.StoredNodeSummary = (6, 39);
        elements[601].Geometry = new GeometryData { StoredNodeSummary = (6, 12) };
        string text = Write(elements, "IFCMEMBER");
        Assert.Single(Lines(text, "IFCMEMBERTYPE"));
    }

    [Fact]
    public void NestedOrUnvalidatedGeometry_DoesNotInventAnOriginalSymbol()
    {
        var elements = Elements(601, 602);
        foreach (var element in elements.Values.Where(e => e.Id is 1 or 2))
        {
            element.Geometry!.Instances = element.Geometry.ExactMappedInstances;
            element.Geometry.ExactMappedInstances = null;
        }
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY");
        Assert.Single(Lines(text, "IFCBUILDINGELEMENTPROXYTYPE"));
        Assert.Contains($"'{IfcGuid.FromName("type:500")}'", text);
    }

    [Fact]
    public void MappedAndCanonicalGroups_HaveDifferentSyntheticIdentities()
    {
        var elements = Elements(601, 601);
        elements[2].Geometry = null;
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY");
        var types = Lines(text, "IFCBUILDINGELEMENTPROXYTYPE");
        Assert.Equal(2, types.Length);
        Assert.Equal(2, types.Select(line => line.Split(',')[0]).Distinct().Count());
        Assert.Contains($"'{IfcGuid.FromName("type:500")}'", text);
    }

    private static Dictionary<int, Element> Elements(int firstSymbol, int secondSymbol)
    {
        Element Instance(int id, int symbol) => new()
        {
            Id = id, NativeMasterSymbolId = 500,
            Parameters = [new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = 500,
            }],
            Geometry = new GeometryData { ExactMappedInstances = [new GeoInstance { SymbolId = symbol }] },
        };
        return new Dictionary<int, Element>
        {
            [1] = Instance(1, firstSymbol), [2] = Instance(2, secondSymbol),
            [500] = new() { Id = 500, ValidatedIfcTypeObjectName = "Own family:Own type" },
            [601] = new() { Id = 601, Name = "Nested symbol name" },
            [602] = new() { Id = 602, Name = "Another nested symbol" },
        };
    }

    private static string Write(Dictionary<int, Element> elements, string product, bool reverse = false, bool currentProfile = false)
    {
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)>();
        foreach (int id in reverse ? new[] { 2, 1 } : new[] { 1, 2 })
            products.Add(id, (1000 + id, 0, product));
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByTypeCore", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>(), currentProfile]);
        using var output = new StringWriter();
        writer.WriteTo(output, "test.ifc", "IFC2X3", "test", "test");
        return output.ToString();
    }

    private static string[] Lines(string text, string type) => text.Split('\n')
        .Where(line => Regex.IsMatch(line, $@"^#\d+={type}\(")).ToArray();

    private static int Id(string line) => int.Parse(line[1..line.IndexOf('=')]);
}
