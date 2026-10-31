using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Pset_WallCommon / Pset_SlabCommon ThermalTransmittance of the current profile: 1 / sum(width / conductivity of the thermal asset of the layer material) over the compound structure of the type; a layer without a thermal asset adds nothing and a type without any such layer has no value.</summary>
public class IfcExporterCurrentThermalTransmittanceTests
{
    private const int EmissivityParamId = -1152320;
    private const int ConductivityParamId = -1152308;
    private const double ConcreteConductivity = 3.431758530183727;
    private const double InsulationConductivity = 0.08202099737532809;

    private static Element Asset(int id, double? conductivity, bool thermal = true)
    {
        var asset = new Element { Id = id, CategoryId = -2000924 };
        if (thermal) asset.Parameters.Add(new Parameter { Id = EmissivityParamId, StorageType = StorageType.Double, Value = 0.95 });
        if (conductivity != null)
            asset.Parameters.Add(new Parameter { Id = ConductivityParamId, StorageType = StorageType.Double, Value = conductivity.Value });
        return asset;
    }

    private static Element Material(int id, params int[] references) =>
        new() { Id = id, CategoryId = -2000700, References = [id, .. references] };

    private static RawCompoundStructureLayer Layer(double widthMm, int materialId) =>
        new(BitConverter.DoubleToInt64Bits(widthMm / 304.8), 4, 0, materialId, -1, 0, 0);

    private static Element Type(int id, ushort classId, params RawCompoundStructureLayer[] layers) =>
        new() { Id = id, CategoryId = -2000011, SerializedSystemTypeClassId = classId, SerializedHostTypeLayers = layers };

    private static Dictionary<int, Element> Elements(params Element[] elements) => elements.ToDictionary(element => element.Id);

    [Fact]
    public void SingleConcreteLayer_IsTheConductivityOverTheWidth()
    {
        var model = Elements(Material(523, 900), Asset(900, ConcreteConductivity), Type(581500, 512, Layer(300, 523)));
        double? value = IfcExporter.ResolveHostTypeThermalTransmittanceCurrent(model[581500], model);
        Assert.Equal(3.4866666666666668, value!.Value, 12);
    }

    [Fact]
    public void LayersWithoutAThermalAsset_AddNothing()
    {
        var model = Elements(
            Material(416, 901), Asset(901, InsulationConductivity), Material(198374),
            Type(845480, 512, Layer(10, 198374), Layer(20, 416), Layer(220, 416), Layer(20, 416), Layer(10, 198374)));
        double? value = IfcExporter.ResolveHostTypeThermalTransmittanceCurrent(model[845480], model);
        Assert.Equal(0.096153846153846145, value!.Value, 12);
    }

    [Fact]
    public void TypeWithoutAnyThermalLayer_HasNoValue()
    {
        var model = Elements(Material(198374), Type(198367, 512, Layer(22, 198374), Layer(25, 198374)));
        Assert.Null(IfcExporter.ResolveHostTypeThermalTransmittanceCurrent(model[198367], model));
    }

    [Fact]
    public void OnlyZeroWidthThermalLayers_GiveInfinity()
    {
        var model = Elements(Material(581329, 902), Asset(902, 3.772965879265091), Material(581406),
            Type(7000, 1915, Layer(0, 581329), Layer(80, 581406)));
        Assert.Equal(double.PositiveInfinity, IfcExporter.ResolveHostTypeThermalTransmittanceCurrent(model[7000], model));
    }

    [Fact]
    public void StructuralAsset_IsNotAThermalAsset()
    {
        var model = Elements(Material(523, 903), Asset(903, 5.2493438320209975, thermal: false), Type(1, 512, Layer(300, 523)));
        Assert.Null(IfcExporter.ResolveHostTypeThermalTransmittanceCurrent(model[1], model));
    }

    [Fact]
    public void MaterialWithTwoThermalAssets_CannotBeResolved()
    {
        var material = Material(523, 904, 905);
        var model = Elements(material, Asset(904, ConcreteConductivity), Asset(905, InsulationConductivity));
        Assert.Null(IfcExporter.ResolveMaterialThermalConductivityCurrent(material, model));
    }

    [Fact]
    public void RoofType_HasNoTransmittance()
    {
        var model = Elements(Material(123310, 906), Asset(906, 0.39370078740157477), Type(507024, 3452, Layer(100, 123310)));
        Assert.Null(IfcExporter.ResolveHostTypeThermalTransmittanceCurrent(model[507024], model));
    }

    [Fact]
    public void Occurrence_TakesTheValueOfItsOwnType()
    {
        var wall = new Element { Id = 77, CategoryId = -2000011, SerializedSystemTypeId = 581500 };
        var model = Elements(Material(523, 900), Asset(900, ConcreteConductivity), Type(581500, 512, Layer(300, 523)), wall);
        Assert.Equal(3.4866666666666668, IfcExporter.ResolveOccurrenceThermalTransmittanceCurrent(wall, model)!.Value, 12);
        wall.SerializedSystemTypeId = null;
        Assert.Null(IfcExporter.ResolveOccurrenceThermalTransmittanceCurrent(wall, model));
    }

    private static Element FloorProduct(int id, int typeId) => new()
    {
        Id = id, SuperInstanceId = 999, IsDiscovered = true, CategoryId = -2000032, Name = $"Product {id}",
        References = [3],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
            new Parameter { Id = -1002050, Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = typeId },
        ],
        BoundingBox = [0, 0, 0, 4, 3, 2],
        Geometry = SolidPrism(),
    };

    private static GeometryData SolidPrism()
    {
        double[][] points =
        [
            [0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0],
            [0, 0, 2], [4, 0, 2], [4, 3, 2], [0, 3, 2],
        ];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        return new GeometryData
        {
            BoundingBox = [0, 0, 0, 4, 3, 2],
            Faces = faces.Select(indices => new GeoFace { OuterVertices = indices.Select(index => points[index]).ToList() }).ToList(),
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
        };
    }

    private static string ThermalTransmittanceOfSlabCommonSets(IfcProfile version)
    {
        var floorType = new Element { Id = 500, Name = "Concrete 150", FamilyName = "Floor", CategoryId = -2000032,
            SerializedSystemTypeClassId = 1915, SerializedHostTypeLayers = [Layer(150, 600)] };
        var info = new RevitFileInfo();
        info.Elements.AddRange([floorType, Material(600, 601), Asset(601, ConcreteConductivity), FloorProduct(100, 500)]);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-currentProfile-thermal-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            string ifc = File.ReadAllText(path);
            string Describe(string reference)
            {
                var match = Regex.Match(ifc, $@"{reference}=IFCPROPERTYSINGLEVALUE\('([^']*)',[^,]*,(.*),\$\);");
                return match.Success ? $"{match.Groups[1].Value}={match.Groups[2].Value}" : "?";
            }
            return string.Join("|", Regex.Matches(ifc, @"=IFCPROPERTYSET\('[^']*',#\d+,'Pset_SlabCommon',[^,]*,\(([^)]*)\)\);")
                .Select(set => string.Join(",", set.Groups[1].Value.Split(',').Select(Describe))));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FloorTypeSet_CarriesTheTransmittanceOfItsLayers_InCurrent_Only()
    {
        string expected = "ThermalTransmittance=IFCTHERMALTRANSMITTANCEMEASURE(6.9733";
        Assert.Contains(expected, ThermalTransmittanceOfSlabCommonSets(IfcProfile.Current));
        Assert.DoesNotContain("ThermalTransmittance", ThermalTransmittanceOfSlabCommonSets(IfcProfile.Legacy));
    }

    [Fact]
    public void Literal_WritesInfinityTheWayReferenceDoes()
    {
        Assert.Equal("IFCTHERMALTRANSMITTANCEMEASURE(Infinity.)", IfcExporter.ThermalTransmittanceLiteralCurrent(double.PositiveInfinity));
        Assert.Equal("IFCTHERMALTRANSMITTANCEMEASURE(150.)", IfcExporter.ThermalTransmittanceLiteralCurrent(150));
    }
}
