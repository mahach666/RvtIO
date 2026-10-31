using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

public class IfcFoundationSlabPredefinedTypeTests
{
    [Theory]
    [InlineData(-2001300, "arbitrary authored name", "BASESLAB")]
    [InlineData(-2001300, "Floor", "BASESLAB")]
    [InlineData(-2000032, "OST_StructuralFoundation", "FLOOR")]
    [InlineData(-2000920, "Foundation", "LANDING")]
    [InlineData(null, "Foundation", "FLOOR")]
    public void CategoryDefaultDoesNotDependOnNamesOrAnalyticalParameter(int? category, string name, string expected)
    {
        var slab = new Element { Id = 100, CategoryId = category, Name = name, Category = "OST_StructuralFoundation" };
        slab.Parameters.Add(new Parameter { Id = -1001576, StorageType = StorageType.Integer, Value = 4 });
        Assert.Equal(expected, IfcExporter.ResolveSlabOccurrencePredefinedType(slab, null, Counts()));
    }

    [Theory]
    [InlineData("block_only")] [InlineData("duplicate_occurrence")] [InlineData("missing_occurrence")]
    [InlineData("nonpositive_id")] [InlineData("duplicate_type")] [InlineData("wrong_type_category")]
    [InlineData("wrong_type_id")] [InlineData("missing_type")] [InlineData("self_type")]
    [InlineData("nonpositive_type")]
    public void UnvalidatedMaterializedOwnershipRetainsFloor(string mutation)
    {
        var slab = Foundation(); var type = new Element { Id = 200, CategoryId = -2001300 };
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
            case "missing_type": counts.Remove(200); break;
            case "self_type": type.Id = 100; slab.TypeId = 100; break;
            case "nonpositive_type": type.Id = 0; slab.TypeId = 0; break;
        }
        Assert.Equal("FLOOR", IfcExporter.ResolveSlabOccurrencePredefinedType(slab, type, counts));
    }

    [Theory]
    [InlineData(false, "IfcExportAs")] [InlineData(true, "IfcExportType")]
    [InlineData(false, "IfcType[Type]")] [InlineData(true, "IfcExportAs[Type]")]
    [InlineData(false, "IfcType")] [InlineData(true, "IfcExportType[Type]")]
    public void InstanceAndTypeOverridesBlockTheNewCategoryDefault(bool onType, string name)
    {
        var slab = Foundation(); var type = new Element { Id = 200, CategoryId = -2001300 };
        (onType ? type : slab).Parameters.Add(new Parameter
        {
            Definition = new Definition { Name = name }, StorageType = StorageType.String, Value = "USERDEFINED",
        });
        Assert.Equal("FLOOR", IfcExporter.ResolveSlabOccurrencePredefinedType(slab, type, Counts()));
    }

    [Theory]
    [InlineData(false, "BASESLAB")] [InlineData(true, "FLOOR")]
    public void WholeIfcKeepsBodyAndCommonProperties(bool overridden, string expected)
    {
        var info = new RevitFileInfo();
        var slab = IfcExporterSlabCommonPropertySetTests.Slab(100, 200, "Native product", -2001300,
            "OST_StructuralFoundation", null);
        if (overridden) slab.Parameters.Add(new Parameter
        {
            Definition = new Definition { Name = "IfcExportType" }, StorageType = StorageType.String, Value = "USERDEFINED",
        });
        info.Elements.Add(slab);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-foundation-enum-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(info, path);
            string text = File.ReadAllText(path);
            var product = Assert.Single(Regex.Matches(text, @"#\d+=IFCSLAB\(([^\r\n]*)\);"));
            Assert.EndsWith($".{expected}.", product.Groups[1].Value);
            Assert.Matches(@",#\d+,#\d+,'100',\.[A-Z]+\.$", product.Groups[1].Value);
            Assert.Contains("'Pset_SlabCommon'", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Element Foundation() => new() { Id = 100, TypeId = 200, CategoryId = -2001300 };
    private static Dictionary<int, int> Counts() => new() { [100] = 1, [200] = 1 };
}
