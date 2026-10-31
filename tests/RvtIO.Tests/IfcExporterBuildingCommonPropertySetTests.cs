using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// Pset_BuildingCommon: NumberOfStoreys only, attached unconditionally to the single emitted IFCBUILDING (not gated
/// on QTO success like the per-element common Psets). The value is the count of IFCBUILDINGSTOREY entities built from
/// the parsed level structure.
///
/// IsLandmarked and Pset_BuildingStoreyCommon.AboveGround are never emitted: the expected export always writes
/// IFCLOGICAL(.U.) for them and the file has no flag to derive them from, so a constant would be invented data.
/// </summary>
public class IfcExporterBuildingCommonPropertySetTests
{
    private const string BuildingPsetName = "Pset_BuildingCommon";

    [Fact]
    public void NoLevels_BuildingCommonHasNumberOfStoreysZero()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Product(id: 100, typeId: 101, categoryId: -2001330, name: "Column"));
        string path = TempIfcPath("buildingcommon-no-levels");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.Single(Regex.Matches(ifc, $"'{BuildingPsetName}'"));
            int buildingId = SoleEntityId(ifc, "IFCBUILDING");
            var (psetId, valueIds) = SolePsetOfName(ifc, BuildingPsetName);
            Assert.Equal(buildingId, SoleRelDefinesByPropertiesTarget(ifc, psetId));

            var props = ResolveProperties(ifc, valueIds);
            Assert.Equal(new[] { "NumberOfStoreys" }, props.Keys);
            // No building-storey Level => the current profile writes no storey (sample structural project 2020 to 2026: NumberOfStoreys 0).
            Assert.Equal("IFCINTEGER(0)", props["NumberOfStoreys"]);
            Assert.DoesNotContain("IsLandmarked", ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void NoLevels_Legacy_KeepsTheDefaultStorey()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Product(id: 100, typeId: 101, categoryId: -2001330, name: "Column"));
        string path = TempIfcPath("buildingcommon-no-levels-228");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);
            var (_, valueIds) = SolePsetOfName(ifc, BuildingPsetName);
            Assert.Equal("IFCINTEGER(1)", ResolveProperties(ifc, valueIds)["NumberOfStoreys"]);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void ThreeBuildingStoreyLevels_NumberOfStoreysMatchesLevelCount()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Level(id: 10, name: "L1", elevation: 0));
        info.Elements.Add(Level(id: 11, name: "L2", elevation: 10));
        info.Elements.Add(Level(id: 12, name: "L3", elevation: 20));
        info.Elements.Add(Product(id: 100, typeId: 101, categoryId: -2001330, name: "Column"));
        string path = TempIfcPath("buildingcommon-three-levels");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.Equal(3, Regex.Matches(ifc, "=IFCBUILDINGSTOREY\\(").Count);
            var (_, valueIds) = SolePsetOfName(ifc, BuildingPsetName);
            var props = ResolveProperties(ifc, valueIds);
            Assert.Equal("IFCINTEGER(3)", props["NumberOfStoreys"]);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void PsetBuildingStoreyCommon_IsNeverEmitted()
    {
        // AboveGround is Pset_BuildingStoreyCommon's only reference property and has zero
        // RVT-side signal (see class remarks) - the whole Pset is deliberately omitted.
        var info = new RevitFileInfo();
        info.Elements.Add(Level(id: 20, name: "Ground", elevation: 0));
        info.Elements.Add(Product(id: 200, typeId: 201, categoryId: -2001330, name: "Column"));
        string path = TempIfcPath("buildingcommon-no-storeycommon");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.DoesNotContain("Pset_BuildingStoreyCommon", ifc);
            Assert.DoesNotContain("AboveGround", ifc);
        }
        finally { Cleanup(path); }
    }


    private static Element Level(int id, string name, double elevation) => new()
    {
        Id = id,
        TypeId = id + 1000,
        CategoryId = -2000240,
        Category = "OST_Levels",
        Name = name,
        IsNativeLevel = true,
        SerializedFamilyId = -1,
        SerializedUnplacedOwnerId = -1,
        SerializedOwnerDbViewId = -1,
        Parameters =
        [
            new Parameter { Id = -1007102, StorageType = StorageType.Double, Value = elevation },
            new Parameter { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
        ],
    };

    private static Element Product(
        int id, int typeId, int categoryId, string? name, double offsetX = 0) => new()
    {
        Id = id,
        TypeId = typeId,
        IsDiscovered = true,
        CategoryId = categoryId,
        Category = "OST_StructuralColumns",
        Name = name,
        References = [3],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
        ],
        BoundingBox = [offsetX, 0, 0, offsetX + 4, 3, 2],
        Geometry = SolidPrism(offsetX),
    };

    private static GeometryData SolidPrism(double offsetX, double height = 2)
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
