using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class IfcCeilingCanonicalMaterialsTests
{
    [Theory]
    [InlineData("reference")] [InlineData("parameter")] [InlineData("geometry_style")]
    public void ValidatedNativeCeiling_DoesNotTurnTypeReferencesIntoDirectMaterialAssignments(string source)
    {
        var (ceiling, type, material) = Scene();
        type.References = source == "reference" ? [700] : [];
        if (source == "parameter") type.Parameters.Add(MaterialParameter());
        if (source == "geometry_style") ceiling.Geometry!.GStyleElemId = 700;
        string ifc = Export(ceiling, type, material);
        Assert.Contains("IFCCOVERING(", ifc);
        Assert.Contains("IFCCOVERINGTYPE(", ifc);
        Assert.DoesNotContain("IFCRELASSOCIATESMATERIAL(", ifc);
        Assert.DoesNotContain("IFCMATERIAL(", ifc);
    }

    [Fact]
    public void ExistingLayerSetPath_IsPreservedForAValidatedNativeCeiling()
    {
        var (ceiling, type, material) = Scene();
        ceiling.Layers = [new MaterialLayer { LayerIndex = 0, MaterialId = 700, MaterialName = "Own layer", ThicknessMm = 75 }];
        string ifc = Export(ceiling, type, material);
        Assert.Contains("IFCMATERIALLAYERSET(", ifc);
        Assert.Contains("IFCMATERIALLAYERSETUSAGE(", ifc);
        Assert.Contains("IFCRELASSOCIATESMATERIAL(", ifc);
        Assert.DoesNotContain("IFCMATERIALLIST(", ifc);
    }

    [Theory]
    [InlineData("missing_link")] [InlineData("missing_type_marker")]
    [InlineData("wrong_type_marker")] [InlineData("wrong_type_category")]
    [InlineData("different_product_category")]
    public void OtherOrUnvalidatedPaths_RetainTheirPreviousMaterialBehaviour(string mutation)
    {
        var (ceiling, type, material) = Scene();
        switch (mutation)
        {
            case "missing_link": ceiling.SerializedSystemTypeId = null; break;
            case "missing_type_marker": type.SerializedSystemTypeClassId = null; break;
            case "wrong_type_marker": type.SerializedSystemTypeClassId = 512; break;
            case "wrong_type_category": type.CategoryId = -2000032; break;
            case "different_product_category": ceiling.CategoryId = -2000151; ceiling.Category = "OST_GenericModel"; break;
        }
        string ifc = Export(ceiling, type, material);
        Assert.Contains("IFCMATERIAL('Type reference material')", ifc);
        Assert.Contains("IFCRELASSOCIATESMATERIAL(", ifc);
    }

    [Fact]
    public void InstanceMaterialValue_ReplacesTheTypeValueOfTheSameParameter()
    {
        var (product, type, material) = Scene();
        product.CategoryId = -2000151; product.Category = "OST_GenericModel";
        var instanceMaterial = new Element { Id = 710, TypeId = -1, IsBlockOnly = true, CategoryId = -2000700,
            Category = "OST_Materials", Name = "Instance override material", HasShadingAppearance = true,
            Parameters = material.Parameters.Select(parameter => new Parameter
                { Id = parameter.Id + 10, Definition = parameter.Definition, Value = parameter.Value, StorageType = parameter.StorageType }).ToList() };
        type.References = [700, 710];
        type.Parameters.Add(new Parameter { Id = 90001, StorageType = StorageType.ElementId, Value = 700 });
        product.Parameters.Add(new Parameter { Id = 90001, StorageType = StorageType.ElementId, Value = 710 });
        string ifc = Export(product, type, material, instanceMaterial);
        Assert.Contains("IFCMATERIAL('Instance override material')", ifc);
        Assert.DoesNotContain("IFCMATERIAL('Type reference material')", ifc);
    }

    private static (Element, Element, Element) Scene()
    {
        double[][] points = [[0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0],
            [0, 0, 2], [4, 0, 2], [4, 3, 2], [0, 3, 2]];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        var ceiling = new Element
        {
            Id = 100, TypeId = 500, CategoryId = -2000038, Category = "OST_Ceilings",
            Name = "Native ceiling", IsDiscovered = true, References = [3], SerializedSystemTypeId = 500,
            Parameters = [new Parameter { Id = -1002050, Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" },
                Value = 500, StorageType = StorageType.ElementId },
                new Parameter { Id = -1012100, Value = 3, StorageType = StorageType.ElementId }],
            BoundingBox = [0, 0, 0, 4, 3, 2],
            Geometry = new GeometryData { BoundingBox = [0, 0, 0, 4, 3, 2],
                Faces = faces.Select(indices => new GeoFace { OuterVertices = indices.Select(index => points[index]).ToList() }).ToList(),
                BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }] },
        };
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2000038,
            Name = "Native type", SerializedSystemTypeClassId = 646, References = [700] };
        var material = new Element { Id = 700, TypeId = -1, IsBlockOnly = true, CategoryId = -2000700,
            Category = "OST_Materials", Name = "Type reference material", HasShadingAppearance = true, Parameters = [
                new Parameter { Id = 701, Definition = new Definition { Name = "MATERIAL_PARAM_COLOR" }, Value = 0x112233, StorageType = StorageType.Integer },
                new Parameter { Id = 702, Definition = new Definition { Name = "MATERIAL_PARAM_SHININESS" }, Value = 25, StorageType = StorageType.Integer }] };
        return (ceiling, type, material);
    }

    private static Parameter MaterialParameter() => new()
    {
        Id = -1005500, Definition = new Definition { Name = "STRUCTURAL_MATERIAL_PARAM" },
        Value = 700, StorageType = StorageType.ElementId,
    };

    // These tests fix the legacy profile reading of the material references of a family product (parameters and the records
    // of the type); the current profile reading by solids is covered by IfcMepMaterialsTests.
    private static string Export(params Element[] elements)
    {
        var info = new RevitFileInfo();
        info.Elements.AddRange(elements);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-ceiling-canonical-materials-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
