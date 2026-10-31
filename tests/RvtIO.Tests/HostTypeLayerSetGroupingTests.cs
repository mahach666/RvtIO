using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class HostTypeLayerSetGroupingTests
{
    [Theory]
    [InlineData("same", 1)] [InlineData("different", 2)]
    // The expected export caches a layer set per type element (the layer-set cache), so two
    // distinct types never share a set even when their layers are identical or their name is unknown.
    [InlineData("partial", 2)] [InlineData("none", 2)]
    public void FullExporter_PartitionsOnlyValidatedOwnTypes_AndPreservesLayerContents(string validation, int expectedSets)
    {
        var first = Product(100, 500);
        var second = Product(101, validation == "same" ? 500 : 501);
        var a = Type(500); var b = Type(501);
        if (validation == "none") a.ValidatedHostTypeLayerSetName = null;
        if (validation is "none" or "partial") b.ValidatedHostTypeLayerSetName = null;
        PrepareWall(first); PrepareWall(second);
        var info = new RevitFileInfo { Elements = [a, b, first, second] };
        string path = Path.Combine(Path.GetTempPath(), $"rr-own-layer-groups-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter() { Profile = IfcProfile.Legacy }.Export(info, path);
            string text = File.ReadAllText(path);
            Assert.Equal(2, Regex.Matches(text, @"#\d+=IFCWALL(?:STANDARDCASE)?\(").Count);
            Assert.Equal(expectedSets, Regex.Matches(text, @"#\d+=IFCMATERIALLAYERSET\(").Count);
            Assert.Equal(expectedSets, Regex.Matches(text, @"#\d+=IFCMATERIALLAYERSETUSAGE\(").Count);
            var layers = Regex.Matches(text, @"#\d+=IFCMATERIALLAYER\((.*)\);");
            Assert.Equal(expectedSets, layers.Count);
            foreach (Match layer in layers)
            {
                string[] args = IfcExporter.SplitStepArguments(layer.Groups[1].Value);
                Assert.Equal("76.200000000000003", args[1]); Assert.Equal("$", args[2]);
            }
            var assignments = Regex.Matches(text, @"#\d+=IFCRELASSOCIATESMATERIAL\((.*)\);");
            Assert.Equal(expectedSets, assignments.Count);
            Assert.Equal(2, assignments.Sum(assignment =>
                IfcExporter.SplitStepArguments(IfcExporter.SplitStepArguments(assignment.Groups[1].Value)[4][1..^1]).Length));
            foreach (Match usage in Regex.Matches(text, @"#\d+=IFCMATERIALLAYERSETUSAGE\((.*)\);"))
                Assert.Equal(new[] { ".AXIS2.", ".NEGATIVE.", "38.100000000000001" },IfcExporter.SplitStepArguments(usage.Groups[1].Value)[1..]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("category_material", "Default Wall")]
    [InlineData("two_materials", "<Unnamed>")] [InlineData("no_material", "<Unnamed>")] [InlineData("missing_element", "<Unnamed>")]
    [InlineData("own_material", "Own Layer")]
    public void LayerWithoutAMaterial_TakesTheMaterialOfItsCategory(string mode, string expectedName)
    {
        var type = Type(500);
        if (mode != "own_material") type.SerializedHostTypeLayers = [Raw(-1)];
        else type.SerializedHostTypeLayers = [Raw(701)];
        var product = Product(100, 500);
        PrepareWall(product);
        var materials = new List<Element>
        {
            new() { Id = 700, Name = "Default Wall", SerializedMaterialName = "Default Wall" },
            new() { Id = 701, Name = "Own Layer", SerializedMaterialName = "Own Layer" },
            new() { Id = 702, Name = "Other", SerializedMaterialName = "Other" },
        };
        var info = new RevitFileInfo { Elements = [type, product, .. materials] };
        info.GraphicsStyles = mode switch
        {
            "two_materials" => new Dictionary<int, (int Material, int Category)> { [30] = (700, -2000011), [31] = (702, -2000011) },
            "no_material" => new Dictionary<int, (int Material, int Category)> { [30] = (-1, -2000011), [31] = (700, -2000032) },
            "missing_element" => new Dictionary<int, (int Material, int Category)> { [30] = (999, -2000011) },
            _ => new Dictionary<int, (int Material, int Category)> { [30] = (700, -2000011), [31] = (700, -2000011) },
        };
        string path = Path.Combine(Path.GetTempPath(), $"rr-layer-category-material-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter() { Profile = IfcProfile.Legacy }.Export(info, path);
            string text = File.ReadAllText(path);
            var layer = Regex.Match(text, @"#\d+=IFCMATERIALLAYER\(#(\d+),");
            Assert.True(layer.Success);
            Assert.Contains($"#{layer.Groups[1].Value}=IFCMATERIAL('{expectedName}');", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void EqualContentsAndNames_DoNotMergeDifferentValidatedOwnTypes()
    {
        var first = Product(100, 500);
        var second = Product(101, 501);
        var types = new Dictionary<int, Element> { [500] = Type(500), [501] = Type(501) };
        var counts = Counts();
        string before = JsonSerializer.Serialize(new[] { first, second, types[500], types[501] });
        string a = IfcExporter.MaterialLayerSetGroupKey(first, "IFCWALL", types, counts);
        string b = IfcExporter.MaterialLayerSetGroupKey(second, "IFCWALL", types, counts);
        Assert.Equal("AXIS2|700:76.2|type:500", a);
        Assert.Equal("AXIS2|700:76.2|type:501", b);
        Assert.NotEqual(a, b);
        second.SerializedSystemTypeId = 500;
        Assert.Equal(a, IfcExporter.MaterialLayerSetGroupKey(second, "IFCWALLSTANDARDCASE", types, counts));
        second.SerializedSystemTypeId = 501;
        Assert.Equal(before, JsonSerializer.Serialize(new[] { first, second, types[500], types[501] }));
    }

    [Theory]
    [InlineData("missing_link")] [InlineData("missing_type")] [InlineData("missing_name")]
    [InlineData("missing_layers")] [InlineData("different_material")] [InlineData("different_width")]
    [InlineData("duplicate_occurrence")] [InlineData("duplicate_type")]
    [InlineData("wrong_category")] [InlineData("wrong_class")] [InlineData("override")]
    public void IncompleteOwnValidation_RetainsExactLegacyKey(string mode)
    {
        var product = Product(100, 500);
        var type = Type(500);
        var types = new Dictionary<int, Element> { [500] = type };
        var counts = Counts();
        string ifcType = "IFCWALL";
        switch (mode)
        {
            case "missing_link": product.SerializedSystemTypeId = null; break;
            case "missing_type": types.Clear(); break;
            case "missing_name": type.ValidatedHostTypeLayerSetName = null; break;
            case "missing_layers": type.SerializedHostTypeLayers = null; break;
            case "different_material": type.SerializedHostTypeLayers![0] = Raw(701); break;
            case "different_width": type.SerializedHostTypeLayers![0] = Raw(700) with { LayerWidthBits = BitConverter.DoubleToInt64Bits(0.5) }; break;
            case "duplicate_occurrence": counts[100] = 2; break;
            case "duplicate_type": counts[500] = 2; break;
            case "wrong_category": type.CategoryId = -2000032; break;
            case "wrong_class": ifcType = "IFCCURTAINWALL"; break;
            case "override": type.Parameters.Add(new Parameter { Definition = new Definition { Name = "IfcMaterialLayerSet.Name" } }); break;
        }
        Assert.Equal((ifcType == "IFCCURTAINWALL" ? "AXIS3" : "AXIS2") + "|700:76.2",
            IfcExporter.MaterialLayerSetGroupKey(product, ifcType, types, counts));
    }

    [Fact]
    public void ValidatedAndUnvalidatedOccurrences_AreNotMerged()
    {
        var first = Product(100, 500);
        var second = Product(101, 501);
        var types = new Dictionary<int, Element> { [500] = Type(500) };
        Assert.NotEqual(IfcExporter.MaterialLayerSetGroupKey(first, "IFCWALL", types, Counts()),
            IfcExporter.MaterialLayerSetGroupKey(second, "IFCWALL", types, Counts()));
    }

    [Fact]
    public void GroupKey_UsesInvariantWidthAndTypeId()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("AXIS2|700:76.2|type:500", IfcExporter.MaterialLayerSetGroupKey(
                Product(100, 500), "IFCWALL", new Dictionary<int, Element> { [500] = Type(500) }, Counts()));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    private static Dictionary<int, int> Counts() => new() { [100] = 1, [101] = 1, [500] = 1, [501] = 1 };
    private static RawCompoundStructureLayer Raw(int material) => new(BitConverter.DoubleToInt64Bits(0.25), 1, -1, material, -1, 0, 1);
    private static Element Type(int id) => new() { Id = id, CategoryId = -2000011,
        SerializedSystemTypeClassId = 512, SerializedHostTypeLayers = [Raw(700)],
        ValidatedHostTypeLayerSetName = "Same family:Same name" };
    private static Element Product(int id, int typeId) => new() { Id = id, CategoryId = -2000011,
        SerializedSystemTypeId = typeId, Layers = [new MaterialLayer { LayerIndex = 0, MaterialId = 700, ThicknessMm = 76.2 }] };

    private static void PrepareWall(Element element)
    {
        element.IsDiscovered = true; element.Category = "OST_Walls";
        element.Name = $"Legacy {element.Id}";
        element.Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
            new Parameter { Id = -1002050, StorageType = StorageType.ElementId, Value = element.SerializedSystemTypeId }];
        double[][] points = [[0,0,0],[4,0,0],[4,3,0],[0,3,0],[0,0,2],[4,0,2],[4,3,2],[0,3,2]];
        int[][] faces = [[0,3,2,1],[4,5,6,7],[0,1,5,4],[1,2,6,5],[2,3,7,6],[3,0,4,7]];
        element.BoundingBox = [0,0,0,4,3,2];
        element.Geometry = new GeometryData { BoundingBox = element.BoundingBox,
            Faces = faces.Select(indices => new GeoFace { OuterVertices = indices.Select(index => points[index]).ToList() }).ToList(),
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0,1,2,3,4,5] }] };
    }
}
