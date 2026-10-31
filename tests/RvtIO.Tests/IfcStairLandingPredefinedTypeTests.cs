using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

public class IfcStairLandingPredefinedTypeTests
{
    [Theory]
    [InlineData(-2000920, "unrelated arbitrary name", "LANDING")]
    [InlineData(-2000920, "Площадка", "LANDING")]
    [InlineData(-2000032, "OST_StairsLandings", "FLOOR")]
    [InlineData(-2001300, "Landing", "BASESLAB")]
    [InlineData(null, "Landing", "FLOOR")]
    public void OnlyIntrinsicCategoryAuthorizesTheNativeOccurrenceEnum(int? category, string name, string expected)
    {
        var slab = new Element { Id = 100, CategoryId = category, Name = name, Category = "OST_StairsLandings" };
        Assert.Equal(expected, IfcExporter.ResolveSlabOccurrencePredefinedType(slab, null, Counts()));
    }

    [Theory]
    [InlineData("block_only")] [InlineData("duplicate_occurrence")] [InlineData("missing_occurrence")]
    [InlineData("nonpositive_id")] [InlineData("duplicate_type")] [InlineData("wrong_type_category")]
    [InlineData("wrong_type_id")]
    public void AmbiguousMaterializedOwnershipRetainsThePreviousEnum(string mutation)
    {
        var slab = Landing(); var type = new Element { Id = 200, CategoryId = -2000920 };
        var counts = Counts();
        switch (mutation)
        {
            case "block_only": slab.IsBlockOnly = true; break;
            case "duplicate_occurrence": counts[100] = 2; break;
            case "missing_occurrence": counts.Remove(100); break;
            case "nonpositive_id": slab.Id = 0; break;
            case "duplicate_type": counts[200] = 2; break;
            case "wrong_type_category": type.CategoryId = -2000032; break;
            case "wrong_type_id": type.Id = 201; counts[201] = 1; break;
        }
        Assert.Equal("FLOOR", IfcExporter.ResolveSlabOccurrencePredefinedType(slab, type, counts));
    }

    [Theory]
    [InlineData(false, "IfcExportAs")] [InlineData(true, "IfcExportType")]
    [InlineData(false, "IfcType[Type]")] [InlineData(true, "IfcExportAs[Type]")]
    public void InstanceAndTypeOverridePresenceBlocksThisNewDefault(bool onType, string name)
    {
        var slab = Landing(); var type = new Element { Id = 200, CategoryId = -2000920 };
        (onType ? type : slab).Parameters.Add(new Parameter
        {
            Definition = new Definition { Name = name }, StorageType = StorageType.String, Value = "IfcSlab.USERDEFINED",
        });
        Assert.Equal("FLOOR", IfcExporter.ResolveSlabOccurrencePredefinedType(slab, type, Counts()));
    }

    [Theory]
    [InlineData(-2000920, "LANDING")] [InlineData(-2000032, "FLOOR")]
    public void WholeIfcWritesTheNativeEnumWithoutChangingProductGeometryOrProperties(int category, string expected)
    {
        var info = new RevitFileInfo();
        info.Elements.Add(IfcExporterSlabCommonPropertySetTests.Slab(100, 200, "Native product", category,
            category == -2000920 ? "OST_StairsLandings" : "OST_Floors", null));
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-slab-enum-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(info, path);
            var product = Assert.Single(Regex.Matches(File.ReadAllText(path), @"#\d+=IFCSLAB\(([^\r\n]*)\);"));
            Assert.EndsWith($".{expected}.", product.Groups[1].Value);
            Assert.Matches(@",#\d+,#\d+,'100',\.[A-Z]+\.$", product.Groups[1].Value);
            Assert.Contains("'Pset_SlabCommon'", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Element Landing() => new() { Id = 100, TypeId = 200, CategoryId = -2000920 };
    private static Dictionary<int, int> Counts() => new() { [100] = 1, [200] = 1 };
}
