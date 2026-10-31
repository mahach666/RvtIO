using System.Globalization;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Material association rules of the expected export (host objects, layer sets, body materials).</summary>
public class HostMaterialUsageTests
{
    private const double WidthFeet = 0.25;

    private static string Export(params Element[] elements)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rr-host-material-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter() { Profile = IfcProfile.Legacy }.Export(new RevitFileInfo { Elements = [.. elements] }, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string[] UsageArguments(string text)
    {
        var usage = Regex.Matches(text, @"#\d+=IFCMATERIALLAYERSETUSAGE\((.*)\);").Single();
        return IfcExporter.SplitStepArguments(usage.Groups[1].Value)[1..];
    }

    [Theory]
    [InlineData(false, ".NEGATIVE.", 1)]
    [InlineData(true, ".POSITIVE.", -1)]
    public void WallUsage_FollowsTheWallFlip(bool flipped, string sense, int offsetSign)
    {
        var type = WallType(500);
        var wall = Wall(100, 500);
        wall.SerializedWallFlipped = flipped;
        string[] usage = UsageArguments(Export(type, wall));
        Assert.Equal(".AXIS2.", usage[0]);
        Assert.Equal(sense, usage[1]);
        // the file unit is MILLI METRE: half of 0.25 ft * 304.8, 17 digits
        Assert.Equal(offsetSign < 0 ? "-38.100000000000001" : "38.100000000000001", usage[2]);
    }

    [Fact]
    public void WallUsageOffset_ScalesTheStoredWallWidthOnce()
    {
        // The stored wall width (WALL_ATTR_WIDTH_PARAM) is one ulp above the double nearest 0.02 m; the
        // reference scales it once and halves it: 10.000000000000002 mm, not the layer-based 10.0 mm
        // (the file unit is MILLI METRE).
        const double storedFeet = 0.06561679790026248;
        var wall = Wall(100, 500);
        wall.Parameters.Add(new Parameter { Id = -1001000, StorageType = StorageType.Double, Value = storedFeet });
        string[] usage = UsageArguments(Export(WallType(500), wall));
        Assert.Equal("10.000000000000002", usage[2]);
    }

    [Fact]
    public void LengthsKeepSeventeenDigitsOfTheMillimetreValue()
    {
        Assert.Equal("0.25000000000000003", StepWriter.RealFromMillimetres(0.8202099737532809 * 304.8));
        Assert.Equal("0.095000000000000014", StepWriter.RealFromMillimetres(0.6233595800524935 * 304.8 / 2));
        Assert.Equal("0.02", StepWriter.RealFromMillimetres(20.0));
        Assert.Equal("0.", StepWriter.RealFromMillimetres(0));
    }

    [Fact]
    public void LayerWidth_IsWrittenUnrounded()
    {
        string text = Export(WallType(500), Wall(100, 500));
        var layer = Regex.Matches(text, @"#\d+=IFCMATERIALLAYER\((.*)\);").Single();
        // 0.25 ft * 304.8 mm with all 17 digits, in the MILLI METRE file unit
        Assert.Equal("76.200000000000003", IfcExporter.SplitStepArguments(layer.Groups[1].Value)[1]);
    }

    [Fact]
    public void LayerSet_IsNamedAfterTheType_AndSharedByItsInstances()
    {
        var type = WallType(500);
        var first = Wall(100, 500);
        var second = Wall(101, 500);
        string text = Export(type, first, second);
        var sets = Regex.Matches(text, @"#\d+=IFCMATERIALLAYERSET\((.*)\);");
        Assert.Single(sets);
        Assert.EndsWith("'Basic Wall:Generic'", sets[0].Groups[1].Value);
    }

    [Theory]
    [InlineData("Arc", 5.0, "IFCWALLSTANDARDCASE")]
    [InlineData("Line", null, "IFCWALLSTANDARDCASE")]
    [InlineData("Spline", null, "IFCWALL")]
    public void WallAxis_LineOrArcKeepsStandardCase(string curveType, double? radius, string expectedClass)
    {
        // The expected export allows a line or an arc as the axis of a StandardCase wall: all 20 of its arc walls are.
        var wall = Wall(100, 500);
        wall.Location = new LocationCurve { CurveType = curveType, ArcRadius = radius, Length = 4,
            StartPoint = [0, 0, 0], EndPoint = [4, 0, 0] };
        string text = Export(WallType(500), wall);
        Assert.Matches($@"#\d+={expectedClass}\(", text);
    }

    [Theory]
    [InlineData("Line", "IFCWALLSTANDARDCASE", 1)]
    [InlineData("Spline", "IFCWALL", 0)]
    public void Current_OnlyAStandardCaseWallGetsALayerSetUsage(string curveType, string expectedClass, int usages)
    {
        // The current profile skips the material association of a brep wall (an IfcWall that is not StandardCase).
        var wall = Wall(100, 500);
        wall.Location = new LocationCurve { CurveType = curveType, Length = 4, StartPoint = [0, 0, 0], EndPoint = [4, 0, 0] };
        string path = Path.Combine(Path.GetTempPath(), $"rr-host-material-{Guid.NewGuid():N}.ifc");
        string text;
        try
        {
            new IfcExporter().Export(new RevitFileInfo { Elements = [WallType(500), wall] }, path);
            text = File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }

        string wallId = Regex.Match(text, $@"#(\d+)={expectedClass}\(").Groups[1].Value;
        Assert.NotEmpty(wallId);
        Assert.Equal(usages, Regex.Matches(text, @"=IFCMATERIALLAYERSETUSAGE\(").Count);
        // no material relation of any kind mentions an IfcWall; a StandardCase wall is related exactly once
        Assert.Equal(usages, Regex.Matches(text, $@"=IFCRELASSOCIATESMATERIAL\([^;]*\(#{wallId}\)").Count);
    }

    private static string ExportCurrent(params Element[] elements)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rr-host-material-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(new RevitFileInfo { Elements = [.. elements] }, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("Line", "IFCWALLSTANDARDCASE")]
    [InlineData("Spline", "IFCWALL")]
    public void Current_AWallTypeTakesTheLayerSetItself_AlsoWhenItsWallsHaveNoMaterial(string curveType, string wallClass)
    {
        var wall = Wall(100, 500);
        wall.Location = new LocationCurve { CurveType = curveType, Length = 4, StartPoint = [0, 0, 0], EndPoint = [4, 0, 0] };
        // the type object is emitted for the type the wall names through ELEM_TYPE_PARAM
        wall.Parameters.Single(parameter => parameter.Id == -1002050).Definition = new Definition { Name = "ELEM_TYPE_PARAM" };
        string text = ExportCurrent(WallType(500), wall);

        Assert.Matches($@"#\d+={wallClass}\(", text);
        string layerSet = Regex.Match(text, @"#(\d+)=IFCMATERIALLAYERSET\(").Groups[1].Value;
        string wallType = Regex.Match(text, @"#(\d+)=IFCWALLTYPE\(").Groups[1].Value;
        Assert.NotEmpty(layerSet);
        Assert.NotEmpty(wallType);
        // the relation of the type names the layer set, not a usage
        Assert.Matches($@"=IFCRELASSOCIATESMATERIAL\([^;]*\(#{wallType}\),#{layerSet}\);", text);
    }

    [Fact]
    public void Legacy_NoLayerSetForTheWallType()
    {
        string text = Export(WallType(500), Wall(100, 500));
        string layerSet = Regex.Match(text, @"#(\d+)=IFCMATERIALLAYERSET\(").Groups[1].Value;
        Assert.DoesNotMatch($@"=IFCRELASSOCIATESMATERIAL\([^;]*,#{layerSet}\);", text);
    }

    [Theory]
    [InlineData(true, " <Unnamed>")]
    [InlineData(false, "<Unnamed>")]
    public void NullMaterial_CurrentHasALeadingSpace(bool currentProfile, string expectedName)
    {
        // the layer's material (700) is not an element of the model: the layer takes the null material
        string text = currentProfile ? ExportCurrent(WallType(500), Wall(100, 500)) : Export(WallType(500), Wall(100, 500));
        Assert.Contains($"IFCMATERIAL('{expectedName}')", text);
    }

    [Theory]
    [InlineData(true, ".NEGATIVE.")]
    [InlineData(false, ".POSITIVE.")]
    public void CeilingUsage_IsFlippedInCurrent(bool currentProfile, string sense)
    {
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2000038, SerializedSystemTypeClassId = 646,
            SerializedHostTypeLayers = [Raw(700)], ValidatedHostTypeLayerSetName = "Compound Ceiling:Grid", ValidatedIfcTypeObjectName = "Compound Ceiling:Grid" };
        var ceiling = new Element { Id = 100, TypeId = 500, CategoryId = -2000038, SerializedSystemTypeId = 500, References = [3] };
        Prepare(ceiling, "OST_Ceilings");
        ceiling.Parameters.Single(parameter => parameter.Id == -1002050).Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" };
        string text = currentProfile ? ExportCurrent(type, ceiling) : Export(type, ceiling);

        string[] usage = UsageArguments(text);
        Assert.Equal(".AXIS3.", usage[0]);
        Assert.Equal(sense, usage[1]);
    }

    [Fact]
    public void Current_RoofTakesTheLayerSetItself_WithoutAUsage()
    {
        // The host-object export: a roof is related to the layer set, every other host object to a usage of it.
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2000035, SerializedSystemTypeClassId = 3452,
            SerializedHostTypeLayers = [Raw(700)], ValidatedHostTypeLayerSetName = "Basic Roof:Generic", ValidatedIfcTypeObjectName = "Basic Roof:Generic" };
        var roof = new Element { Id = 100, TypeId = 500, CategoryId = -2000035, SerializedSystemTypeId = 500, References = [3] };
        Prepare(roof, "OST_Roofs");
        roof.Parameters.Single(parameter => parameter.Id == -1002050).Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" };

        string text = ExportCurrent(type, roof);
        string roofId = Regex.Match(text, @"#(\d+)=IFCROOF\(").Groups[1].Value;
        string layerSet = Regex.Match(text, @"#(\d+)=IFCMATERIALLAYERSET\(").Groups[1].Value;
        Assert.NotEmpty(roofId);
        Assert.NotEmpty(layerSet);
        Assert.DoesNotContain("IFCMATERIALLAYERSETUSAGE(", text);
        Assert.Matches($@"=IFCRELASSOCIATESMATERIAL\([^;]*\(#{roofId}\),#{layerSet}\);", text);
        // The legacy profile keeps the roof without a layer set
        Assert.DoesNotContain("IFCMATERIALLAYERSET(", Export(type, roof));
    }

    [Theory]
    [InlineData(true, "Basic Roof : Real name")]
    [InlineData(false, null)]
    public void Current_RoofLayerSet_IsNamedAfterTheValidatedTypeNameNotTheNameField(bool currentProfile, string? expected)
    {
        // The Name field of a roof type can hold another text (the type comments); the set is named after the SymbolInfo name.
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2000035, SerializedSystemTypeClassId = 3452,
            Name = "Comment text", FamilyName = "Basic Roof",
            SerializedHostTypeLayers = [Raw(700)], ValidatedHostTypeLayerSetName = "Basic Roof:Real name" };
        var roof = new Element { Id = 100, TypeId = 500, CategoryId = -2000035, SerializedSystemTypeId = 500, References = [3] };
        Prepare(roof, "OST_Roofs");
        roof.Parameters.Single(parameter => parameter.Id == -1002050).Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" };

        string text = currentProfile ? ExportCurrent(type, roof) : Export(type, roof);
        var set = Regex.Match(text, @"#\d+=IFCMATERIALLAYERSET\(\(.*?\),'(.*?)'\);");
        Assert.Equal(expected, set.Success ? set.Groups[1].Value : null);
    }

    [Fact]
    public void Current_BuildingPadUsage_IsNegativeAxis3()
    {
        // A building pad is not an Floor (its direction sense stays true): Test_2022 "Pad : Pad 2" is
        // IfcMaterialLayerSetUsage(.AXIS3., .NEGATIVE., 0.).
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2001263, SerializedSystemTypeClassId = 571,
            SerializedHostTypeLayers = [Raw(700)], ValidatedHostTypeLayerSetName = "Pad:Generic", ValidatedIfcTypeObjectName = "Pad:Generic" };
        var pad = new Element { Id = 100, TypeId = 500, CategoryId = -2001263, SerializedSystemTypeId = 500, References = [3] };
        Prepare(pad, "OST_BuildingPad");
        pad.Parameters.Single(parameter => parameter.Id == -1002050).Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" };

        string[] usage = UsageArguments(ExportCurrent(type, pad));
        Assert.Equal(".AXIS3.", usage[0]);
        Assert.Equal(".NEGATIVE.", usage[1]);
        // the offset of a flipped usage is -scaledOffset: a pad writes -0.
        Assert.Equal("-0.", usage[2]);
    }

    [Fact]
    public void FoundationSlab_CurrentTakesAUsage_LegacyTheLayerSetItself()
    {
        // The host-object export: only a roof is related to the layer set; a foundation slab is an Floor
        // (Test_2022 "Foundation Slab : 150mm Foundation Slab" is IfcMaterialLayerSetUsage(.AXIS3., .POSITIVE., 0.)).
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2001300, SerializedSystemTypeClassId = 1915,
            SerializedHostTypeLayers = [Raw(700)], ValidatedHostTypeLayerSetName = "Foundation Slab:Generic", ValidatedIfcTypeObjectName = "Foundation Slab:Generic" };
        var slab = new Element { Id = 100, TypeId = 500, CategoryId = -2001300, SerializedSystemTypeId = 500, References = [3] };
        Prepare(slab, "OST_StructuralFoundation");
        slab.Parameters.Single(parameter => parameter.Id == -1002050).Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" };

        string[] usage = UsageArguments(ExportCurrent(type, slab));
        Assert.Equal(".AXIS3.", usage[0]);
        Assert.Equal(".POSITIVE.", usage[1]);
        Assert.Equal("0.", usage[2]);
        string older = Export(type, slab);
        Assert.Contains("IFCMATERIALLAYERSET(", older);
        Assert.DoesNotContain("IFCMATERIALLAYERSETUSAGE(", older);
    }

    [Fact]
    public void Current_FloorOccurrenceOfAFoundationSlabType_TakesTheLayersOfTheType()
    {
        // The occurrence is filed under OST_Floors, its foundation slab type under OST_StructuralFoundation.
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2001300, SerializedSystemTypeClassId = 1915,
            SerializedHostTypeLayers = [Raw(700)], ValidatedHostTypeLayerSetName = "Foundation Slab:Generic", ValidatedIfcTypeObjectName = "Foundation Slab:Generic" };
        var slab = new Element { Id = 100, TypeId = 500, CategoryId = -2000032, SerializedSystemTypeId = 500, References = [3] };
        Prepare(slab, "OST_Floors");
        slab.Parameters.Single(parameter => parameter.Id == -1002050).Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" };

        Assert.Equal(".AXIS3.", UsageArguments(ExportCurrent(type, slab))[0]);
        Assert.DoesNotContain("IFCMATERIALLAYERSET(", Export(type, slab));
    }

    [Theory]
    [InlineData(1, -2001300, true)]    // a foundation slab type of a Floor occurrence
    [InlineData(0, -2001300, false)]   // a floor type filed as a foundation: no rule
    [InlineData(1, -2000032, false)]   // the foundation discriminator on a type under OST_Floors: no rule
    public void FloorOccurrence_TakesAFoundationSlabType_OnlyWhenDiscriminatorAndCategoryAgree(
        int discriminator, int typeCategory, bool validated)
    {
        var state = new ParsingState();
        state.SystemOccurrenceTypeIds[100] = (1, 1913, 500);
        state.SystemTypeRoots[500] = (1, 1915, discriminator);
        var elements = new List<Element> { new() { Id = 100, CategoryId = -2000032 }, new() { Id = 500, CategoryId = typeCategory } };
        ElementBuilder.ApplySerializedSystemTypeIdentity(elements, state);
        Assert.Equal(validated ? 500 : null, elements[0].SerializedSystemTypeId);
    }

    private static RawCompoundStructureLayer Raw(int material) =>
        new(BitConverter.DoubleToInt64Bits(WidthFeet), 1, -1, material, -1, 0, 1);

    private static Element WallType(int id) => new() { Id = id, CategoryId = -2000011,
        SerializedSystemTypeClassId = 512, SerializedHostTypeLayers = [Raw(700)],
        ValidatedHostTypeLayerSetName = "Basic Wall:Generic", ValidatedIfcTypeObjectName = "Basic Wall:Generic" };

    private static Element Wall(int id, int typeId)
    {
        var wall = new Element { Id = id, CategoryId = -2000011, SerializedSystemTypeId = typeId };
        Prepare(wall, "OST_Walls");
        return wall;
    }

    private static void Prepare(Element element, string category)
    {
        element.IsDiscovered = true; element.Category = category;
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
