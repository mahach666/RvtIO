using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Pset_RoofCommon: the product's own Reference and the category IsExternal. Reached only after the same IFCROOF product has emitted Pset_QuantityTakeOff.Reference. IsExternal is a category rule (roofs are always external), not a per-instance parameter read. Without an own type-name validation, legacy Reference is not added. ProjectedArea and TotalArea need a geometry-derived roof face area that this parser does not reproduce, and are asserted absent.</summary>
public class IfcExporterRoofCommonPropertySetTests
{
    private const string RoofPsetName = "Pset_RoofCommon";
    private const int RoofCategoryId = -2000035;

    [Theory]
    [InlineData(-2000035,"IFCROOF",false)] [InlineData(-2000126,"IFCRAILING",false)]
    [InlineData(-2000126,"IFCRAILING",true)]
    public void OwnTypeReferenceSharesTheQtoValueWithCommon(int category, string cls, bool height)
    {
        var info = new RevitFileInfo();
        var element = Roof(950,951,"Public mark");
        element.CategoryId = category;
        element.ValidatedRoofOrRailingTypeReferenceName = "Own type name";
        if (height)
        {
            element.SerializedStairsRailingAttrId = 951;
            element.SerializedRailingTopHeightFeet = 3;
        }
        info.Elements.Add(element);
        string path = TempIfcPath("system-own-reference");
        try
        {
            new IfcExporter().Export(info,path);
            string ifc=File.ReadAllText(path);
            Assert.True(SoleEntityId(ifc,cls)>0);
            var qto=SolePsetOfName(ifc,"Pset_QuantityTakeOff");
            Assert.Equal("IFCIDENTIFIER('Own type name')",ResolveProperties(ifc,qto.valueIds)["Reference"]);
            var common=SolePsetOfName(ifc,cls=="IFCROOF" ? "Pset_RoofCommon" : "Pset_RailingCommon");
            Assert.Contains(qto.valueIds.Single(),common.valueIds);
            var commonProps=ResolveProperties(ifc,common.valueIds);
            Assert.Equal(cls=="IFCROOF" || height ? 2 : 1,commonProps.Count);
            Assert.Equal(height,commonProps.ContainsKey("Height"));
            Assert.Equal("Public mark",element.Name);
            Assert.Equal(951,element.TypeId);
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData(-2000035)] [InlineData(-2000126)]
    public void BlockedOwnReferenceCannotCreateCommonOrQto(int category)
    {
        var info=new RevitFileInfo();
        var element=Roof(950,951,"Legacy fallback");
        element.CategoryId=category;
        element.ValidatedRoofOrRailingTypeReferenceName="Must not escape";
        element.ValidatedRoofOrRailingTypeReferenceIsBlocked=true;
        info.Elements.Add(element);
        string path=TempIfcPath("system-blocked-reference");
        try
        {
            new IfcExporter().Export(info,path);
            string ifc=File.ReadAllText(path);
            Assert.DoesNotContain("Pset_QuantityTakeOff",ifc);
            Assert.DoesNotContain("Pset_RoofCommon",ifc);
            Assert.DoesNotContain("Pset_RailingCommon",ifc);
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("IFCROOF",-2000035,"Old",false,false,"Own")]
    [InlineData("IFCRAILING",-2000126,"Old",false,false,"Own")]
    [InlineData("IFCROOF",-2000126,"Old",false,false,"Old")]
    [InlineData("IFCRAILING",-2000035,"Old",false,false,"Old")]
    [InlineData("IFCROOF",-2000035,null,false,false,null)]
    [InlineData("IFCROOF",-2000035,null,true,false,"Own")]
    [InlineData("IFCRAILING",-2000126,"Old",false,true,null)]
    public void OwnTypeReferencePreservesCategoryClassAndPrecedenceGates(string cls,int category,
        string? baseline,bool explicitValue,bool blocked,string? expected)
    {
        var element=new Element { CategoryId=category,ValidatedRoofOrRailingTypeReferenceName="Own",
            ValidatedRoofOrRailingTypeReferenceIsExplicit=explicitValue,ValidatedRoofOrRailingTypeReferenceIsBlocked=blocked };
        Assert.Equal(expected,IfcExporter.ResolveQuantityTakeOffReferenceValue(element,cls,baseline));
    }

    [Fact]
    public void Roof_GetsPsetRoofCommonWithIsExternalTrue_AndOnlyIsExternal()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Roof(id: 950, typeId: 951, name: "Roof type 'A'"));
        string path = TempIfcPath("roofcommon-exact");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.Single(Regex.Matches(ifc, $"'{RoofPsetName}'"));

            int roofId = SoleEntityId(ifc, "IFCROOF");
            var (psetId, valueIds) = SolePsetOfName(ifc, RoofPsetName);
            Assert.Equal(roofId, SoleRelDefinesByPropertiesTarget(ifc, psetId));

            var props = ResolveProperties(ifc, valueIds);
            Assert.Equal(new[] { "IsExternal" }, props.Keys);
            Assert.Equal("IFCBOOLEAN(.T.)", props["IsExternal"]);

            Assert.DoesNotContain("ProjectedArea", ifc);
            Assert.DoesNotContain("TotalArea", ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Roof_WithHostAreaParameter_GetsProjectedAreaInSquareMetres()
    {
        var info = new RevitFileInfo();
        var roof = Roof(id: 960, typeId: 961, name: "Roof type 'A'");
        roof.Parameters.Add(new Parameter
        {
            Id = -1012805, Definition = new Definition { Name = "HOST_AREA_COMPUTED" },
            StorageType = StorageType.Double, Value = 100.0,
        });
        info.Elements.Add(roof);
        string path = TempIfcPath("roofcommon-projected-area");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);

            var (_, valueIds) = SolePsetOfName(ifc, RoofPsetName);
            var props = ResolveProperties(ifc, valueIds);
            Match area = Regex.Match(props["ProjectedArea"], @"^IFCAREAMEASURE\(([-0-9.E+]+)\)$");
            Assert.True(area.Success);
            Assert.Equal(9.290304, double.Parse(area.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 9);
            Assert.DoesNotContain("TotalArea", ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void TwoRoofs_ShareTheSamePooledIsExternalPropertyEntity()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Roof(id: 960, typeId: 961, name: "Roof A", offsetX: 0));
        info.Elements.Add(Roof(id: 962, typeId: 963, name: "Roof B", offsetX: 20));
        string path = TempIfcPath("roofcommon-pooled");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            var psets = AllPsetsOfName(ifc, RoofPsetName);
            Assert.Equal(2, psets.Count);
            var propsA = ResolveProperties(ifc, psets[0].valueIds);
            var propsB = ResolveProperties(ifc, psets[1].valueIds);
            Assert.Equal(
                psets[0].valueIds[Array.IndexOf([.. propsA.Keys], "IsExternal")],
                psets[1].valueIds[Array.IndexOf([.. propsB.Keys], "IsExternal")]);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void NamelessRoof_GetsNoRoofCommon_NoPlaceholder()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Roof(id: 970, typeId: 971, name: null));
        string path = TempIfcPath("roofcommon-nameless");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
            Assert.DoesNotContain(RoofPsetName, ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void NonRoof_KeepsQtoButReceivesNoRoofCommon()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Product(
            id: 980, typeId: 981, categoryId: -2000032, category: "OST_Floors",
            name: "Floor type"));
        string path = TempIfcPath("roofcommon-non-roof");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.Single(Regex.Matches(ifc, "'Pset_QuantityTakeOff'"));
            Assert.DoesNotContain(RoofPsetName, ifc);
        }
        finally { Cleanup(path); }
    }

    private static Element Roof(int id, int typeId, string? name, double offsetX = 0) => new()
    {
        Id = id,
        TypeId = typeId,
        IsDiscovered = true,
        CategoryId = RoofCategoryId,
        Category = "OST_Roofs",
        Name = name,
        References = [3],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
        ],
        BoundingBox = [offsetX, 0, 0, offsetX + 4, 3, 0.3],
        Geometry = SolidPrism(offsetX),
    };

    private static Element Product(
        int id, int typeId, int categoryId, string category, string? name,
        double offsetX = 0) => new()
    {
        Id = id,
        TypeId = typeId,
        IsDiscovered = true,
        CategoryId = categoryId,
        Category = category,
        Name = name,
        References = [3],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
        ],
        BoundingBox = [offsetX, 0, 0, offsetX + 4, 3, 0.3],
        Geometry = SolidPrism(offsetX),
    };

    private static GeometryData SolidPrism(double offsetX, double height = 0.3)
    {
        double[][] points =
        [
            [offsetX, 0, 0], [offsetX + 4, 0, 0],
            [offsetX + 4, 3, 0], [offsetX, 3, 0],
            [offsetX, 0, height], [offsetX + 4, 0, height],
            [offsetX + 4, 3, height], [offsetX, 3, height],
        ];
        int[][] faces =
        [
            [0, 3, 2, 1], [4, 5, 6, 7],
            [0, 1, 5, 4], [1, 2, 6, 5],
            [2, 3, 7, 6], [3, 0, 4, 7],
        ];
        return new GeometryData
        {
            BoundingBox = [offsetX, 0, 0, offsetX + 4, 3, height],
            Faces = faces.Select(indices => new GeoFace
            {
                OuterVertices = indices.Select(index => points[index]).ToList(),
            }).ToList(),
            BrepSolids =
            [
                new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] },
            ],
        };
    }

    private static string TempIfcPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"revitreader-{tag}-{Guid.NewGuid():N}.ifc");

    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static int SoleEntityId(string ifc, string ifcType)
    {
        var match = Assert.Single(Regex.Matches(ifc, $@"#(\d+)={ifcType}\("));
        return int.Parse(match.Groups[1].Value);
    }

    private static (int psetId, List<int> valueIds) SolePsetOfName(string ifc, string psetName) =>
        Assert.Single(AllPsetsOfName(ifc, psetName));

    private static List<(int psetId, List<int> valueIds)> AllPsetsOfName(string ifc, string psetName)
    {
        var pattern = new Regex(
            $@"#(\d+)=IFCPROPERTYSET\('[^']*',#\d+,'{Regex.Escape(psetName)}',(?:\$|''),\(([^)]*)\)\);");
        var matches = pattern.Matches(ifc);
        Assert.True(matches.Count > 0, $"expected at least one {psetName} IFCPROPERTYSET");
        return matches.Select(m => (
            int.Parse(m.Groups[1].Value),
            Regex.Matches(m.Groups[2].Value, @"#(\d+)")
                .Select(vm => int.Parse(vm.Groups[1].Value)).ToList()
        )).ToList();
    }

    private static int SoleRelDefinesByPropertiesTarget(string ifc, int psetId)
    {
        var pattern = new Regex(
            $@"IFCRELDEFINESBYPROPERTIES\('[^']*',#\d+,(?:\$|''),(?:\$|''),\(#(\d+)\),#{psetId}\);");
        var match = Assert.Single(pattern.Matches(ifc));
        return int.Parse(match.Groups[1].Value);
    }

    private static Dictionary<string, string> ResolveProperties(string ifc, List<int> valueIds)
    {
        var result = new Dictionary<string, string>();
        foreach (int id in valueIds)
        {
            var pattern = new Regex($@"#{id}=IFCPROPERTYSINGLEVALUE\('([^']*)',(?:\$|''),([^,]+(?:\([^)]*\))?),\$\);");
            var match = Assert.Single(pattern.Matches(ifc));
            result[match.Groups[1].Value] = match.Groups[2].Value;
        }
        return result;
    }
}
