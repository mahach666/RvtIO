using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>A wall whose LEVEL_PARAM resolves to a Level that GetBuildingStoreyLevels does not accept as a real project storey is not a product. The predicate keys only on membership in the accepted-storey set, never on the level's id, tag or name, and a wall on an accepted storey is never suppressed. A suppressed wall cascades to the openings hosted on it, which are suppressed and counted with their host.</summary>
public class IfcExporterPhantomStoreyWallSuppressionTests
{
    private const int PhaseId = 3;

    [Fact]
    public void Predicate_KeysOnlyOnAcceptedStoreyMembership()
    {
        var accepted = new Dictionary<int, int> { [1] = 501 }; // level elem id -> storey entity id

        // A positive LEVEL_PARAM absent from the accepted set: suppressed, wall classes only.
        Assert.True(IfcExporter.ShouldSuppressPhantomStoreyWallProduct("IFCWALL", 6, accepted));
        Assert.True(IfcExporter.ShouldSuppressPhantomStoreyWallProduct(
            "IFCWALLSTANDARDCASE", 6, accepted));

        // A positive LEVEL_PARAM present in the accepted set: never suppressed.
        Assert.False(IfcExporter.ShouldSuppressPhantomStoreyWallProduct("IFCWALL", 1, accepted));
        Assert.False(IfcExporter.ShouldSuppressPhantomStoreyWallProduct(
            "IFCWALLSTANDARDCASE", 1, accepted));

        // No resolvable LEVEL_PARAM at all: fail closed, never suppressed. The rule must not
        // treat "no level information" as "phantom level" - only a resolved Level that fails
        // the exporter's own acceptance test earns suppression.
        foreach (int? missing in new int?[] { null, 0, -1, int.MinValue })
            Assert.False(IfcExporter.ShouldSuppressPhantomStoreyWallProduct(
                "IFCWALLSTANDARDCASE", missing, accepted));

        foreach (string other in new[]
                 { "IFCCURTAINWALL", "IFCBUILDINGELEMENTPROXY", "IFCSLAB", "IFCDOOR" })
            Assert.False(IfcExporter.ShouldSuppressPhantomStoreyWallProduct(other, 6, accepted));
    }

    [Fact]
    public void WallOnUnplacedOwnerLevel_YieldsNoProduct_NoContainment_NoPset()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Level(id: 1, name: "Ground", elevation: 0, unplacedOwnerId: -1));
        info.Elements.Add(Level(id: 6, name: "Family reference level", elevation: 0,
            unplacedOwnerId: 199)); // owned by an unplaced family object -> excluded by
        info.Elements.Add(Wall(id: 100, typeId: 101, levelId: 6));
        string path = TempIfcPath("phantom-storey-suppressed");

        try
        {
            var diagnostics = new StringWriter();
            new IfcExporter(diagnostics).Export(info, path);
            string ifc = File.ReadAllText(path);

            // No product...
            Assert.Empty(Regex.Matches(ifc, @"#\d+=IFCWALLSTANDARDCASE\("));
            Assert.Empty(Regex.Matches(ifc, @"#\d+=IFCWALL\("));
            // ...no Pset_QuantityTakeOff...
            Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
            // ...and no spatial containment (the only wall in the file is this one).
            Assert.Empty(Regex.Matches(ifc, @"#\d+=IFCRELCONTAINEDINSPATIALSTRUCTURE\("));

            Assert.Contains(
                "IFC phantom-storey wall products suppressed: 1 - IFCWALLSTANDARDCASE=1",
                diagnostics.ToString());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void WallOnRealAcceptedStorey_IsNeverSuppressed()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Level(id: 1, name: "Ground", elevation: 0, unplacedOwnerId: -1));
        info.Elements.Add(Level(id: 6, name: "Family reference level", elevation: 0,
            unplacedOwnerId: 199));
        info.Elements.Add(Wall(id: 100, typeId: 101, levelId: 1));
        string path = TempIfcPath("phantom-storey-retained");

        try
        {
            var diagnostics = new StringWriter();
            new IfcExporter(diagnostics).Export(info, path);
            string ifc = File.ReadAllText(path);

            var product = Assert.Single(Regex.Matches(ifc, @"#\d+=IFCWALLSTANDARDCASE\("));
            Assert.True(product.Success);
            Assert.Contains("Pset_QuantityTakeOff", ifc);
            Assert.Single(Regex.Matches(ifc, @"#\d+=IFCRELCONTAINEDINSPATIALSTRUCTURE\("));
            Assert.Contains(
                "IFC phantom-storey wall products suppressed: 0", diagnostics.ToString());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void WallOnProjectLevelWithoutBuildingStoryFlag_IsNotSuppressed()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Level(id: 1, name: "Ground", elevation: 0, unplacedOwnerId: -1));
        var plain = Level(id: 7, name: "Datum", elevation: 5, unplacedOwnerId: -1);
        plain.Parameters.Single(p => p.Id == -1007111).Value = 0;
        info.Elements.Add(plain);
        info.Elements.Add(Wall(id: 100, typeId: 101, levelId: 7));
        string path = TempIfcPath("phantom-storey-project-level");

        try
        {
            var diagnostics = new StringWriter();
            new IfcExporter(diagnostics).Export(info, path);

            Assert.Single(Regex.Matches(File.ReadAllText(path), @"#\d+=IFCWALLSTANDARDCASE\("));
            Assert.Contains(
                "IFC phantom-storey wall products suppressed: 0", diagnostics.ToString());
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void HostedOpeningOnPhantomStoreyWall_IsSuppressedWithItsHost()
    {
        // A closed-body generic-model cutter hosted on the wall would normally survive and cut an opening void in the host. With the host suppressed, that void would reference a missing product, so the opening (and only the opening) must be cut too.
        const int wallId = 100;
        const int cutterId = 300;
        var info = new RevitFileInfo();
        info.Elements.Add(Level(id: 1, name: "Ground", elevation: 0, unplacedOwnerId: -1));
        info.Elements.Add(Level(id: 6, name: "Family reference level", elevation: 0,
            unplacedOwnerId: 199));
        info.Elements.Add(Wall(id: wallId, typeId: 101, levelId: 6, cutterId: cutterId));
        info.Elements.Add(ClosedCutter(id: cutterId, hostId: wallId));
        string path = TempIfcPath("phantom-storey-cascade");

        try
        {
            var diagnostics = new StringWriter();
            new IfcExporter(diagnostics).Export(info, path);
            string ifc = File.ReadAllText(path);

            // The host wall: suppressed, as in the single-wall test above.
            Assert.Empty(Regex.Matches(ifc, @"#\d+=IFCWALLSTANDARDCASE\("));
            // The cutter is an independent element with its own closed body - it is not
            // itself suppressed by anything this rule does, only its relationship to the
            // (now absent) host wall is affected.
            Assert.Single(Regex.Matches(ifc, "=IFCBUILDINGELEMENTPROXY\\("));
            // No opening product, no void relationship, no fill relationship: emitting any of
            // these would reference a host product that does not exist.
            Assert.DoesNotContain("=IFCOPENINGELEMENT(", ifc);
            Assert.DoesNotContain("=IFCRELVOIDSELEMENT(", ifc);
            Assert.DoesNotContain("=IFCRELFILLSELEMENT(", ifc);

            Assert.Contains(
                "IFC phantom-storey wall products suppressed: 1 - IFCWALLSTANDARDCASE=1",
                diagnostics.ToString());
            Assert.Contains(
                "IFC phantom-storey wall hosted openings suppressed (cascade): 1",
                diagnostics.ToString());
        }
        finally { Cleanup(path); }
    }

    private static Element Level(int id, string name, double elevation, int unplacedOwnerId) =>
        new()
        {
            Id = id,
            TypeId = id + 1000,
            CategoryId = -2000240,
            Category = "OST_Levels",
            Name = name,
            IsNativeLevel = true,
            SerializedFamilyId = -1,
            SerializedUnplacedOwnerId = unplacedOwnerId,
            SerializedOwnerDbViewId = -1,
            Parameters =
            [
                new Parameter
                {
                    Id = -1007102, StorageType = StorageType.Double, Value = elevation,
                },
                new Parameter
                {
                    Id = -1007111, StorageType = StorageType.Integer, Value = 1,
                },
            ],
        };

    private static Element Wall(int id, int typeId, int levelId, int cutterId = 0) => new()
    {
        Id = id,
        TypeId = typeId,
        CategoryId = -2000011,
        Category = "OST_Walls",
        Name = "Wall",
        References = cutterId > 0 ? [PhaseId, cutterId] : [PhaseId],
        CuttingElementIds = cutterId > 0 ? [cutterId] : [],
        Parameters =
        [
            PhaseParameter(),
            new Parameter
            {
                Id = -1001952,
                Definition = new Definition { Name = "LEVEL_PARAM" },
                StorageType = StorageType.ElementId,
                Value = levelId,
            },
            new Parameter
            {
                Id = -1001000,
                Definition = new Definition { Name = "WALL_ATTR_WIDTH_PARAM" },
                StorageType = StorageType.Double,
                Value = 1.0,
            },
        ],
        Location = new LocationCurve
        {
            CurveType = "Line",
            StartPoint = [0, 0, 0],
            EndPoint = [4, 0, 0],
        },
        BoundingBox = [0, -0.5, 0, 4, 0.5, 3],
    };

    private static Element ClosedCutter(int id, int hostId) => new()
    {
        Id = id,
        TypeId = 301,
        IsDiscovered = true,
        CategoryId = -2000151, // OST_GenericModel -> IFCBUILDINGELEMENTPROXY
        Category = "OST_GenericModel",
        Name = "Cutter proxy",
        FamilyName = "Cutter family",
        SerializedHostId = hostId,
        HostId = hostId,
        References = [hostId, PhaseId],
        Parameters = [PhaseParameter()],
        BoundingBox = [1, -0.6, 1, 2, 0.6, 2],
        Geometry = ClosedCutterBody(),
    };

    private static Parameter PhaseParameter() => new()
    {
        Id = -1012100,
        StorageType = StorageType.Integer,
        Value = PhaseId,
    };

    /// <summary>A closed six-face box: a real closed BRep the geometry-less gate never touches, so any suppression seen here comes from the phantom-storey wall gate alone.</summary>
    private static GeometryData ClosedCutterBody()
    {
        double[][] points =
        [
            [1, -0.6, 1], [2, -0.6, 1], [2, 0.6, 1], [1, 0.6, 1],
            [1, -0.6, 2], [2, -0.6, 2], [2, 0.6, 2], [1, 0.6, 2],
        ];
        int[][] faces =
        [
            [0, 1, 5, 4], [1, 2, 6, 5],
            [2, 3, 7, 6], [3, 0, 4, 7],
            [0, 3, 2, 1], [4, 5, 6, 7],
        ];
        return new GeometryData
        {
            BoundingBox = [1, -0.6, 1, 2, 0.6, 2],
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
}
