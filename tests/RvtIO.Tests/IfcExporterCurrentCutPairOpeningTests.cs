using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The current profile opens a wall for every element that cuts it through a host-to-host pair step
/// (the opening rule over the export-id rule): the finish walls embedded in the
/// sample project A structural walls ("Recess"), a slab cutting a retaining wall of Test_2022 ("Opening"). The legacy profile writes none.
/// </summary>
public class IfcExporterCurrentCutPairOpeningTests
{
    private const int PhaseId = 99;


    private static Element WithBody(int id, double[] start, double[] end, double halfWidth, double z0, double z1)
    {
        double dx = end[0] - start[0], dy = end[1] - start[1], length = Math.Sqrt(dx * dx + dy * dy);
        double nx = -dy / length * halfWidth, ny = dx / length * halfWidth;
        return WithPlan(id, start, end, z0, z1,
        [
            [start[0] - nx, start[1] - ny], [end[0] - nx, end[1] - ny], [end[0] + nx, end[1] + ny], [start[0] + nx, start[1] + ny],
        ]);
    }

    private static Element WithPlan(int id, double[] start, double[] end, double z0, double z1, double[][] plan)
    {
        var faces = new List<GeoFace>
        {
            new() { Normal = [0, 0, -1], Origin = [plan[0][0], plan[0][1], z0], OuterVertices = plan.AsEnumerable().Reverse().Select(p => new[] { p[0], p[1], z0 }).ToList() },
            new() { Normal = [0, 0, 1], Origin = [plan[0][0], plan[0][1], z1], OuterVertices = plan.Select(p => new[] { p[0], p[1], z1 }).ToList() },
        };
        for (int i = 0; i < 4; i++)
        {
            var p = plan[i];
            var q = plan[(i + 1) % 4];
            double sx = q[0] - p[0], sy = q[1] - p[1], sideLength = Math.Sqrt(sx * sx + sy * sy);
            faces.Add(new GeoFace
            {
                Normal = [sy / sideLength, -sx / sideLength, 0],
                OuterVertices = [[p[0], p[1], z0], [q[0], q[1], z0], [q[0], q[1], z1], [p[0], p[1], z1]],
            });
        }
        return new Element
        {
            Id = id,
            Location = new LocationCurve { CurveType = "Line", StartPoint = start, EndPoint = end },
            Geometry = new GeometryData { Faces = faces },
            BoundingBox = [plan.Min(p => p[0]), plan.Min(p => p[1]), z0, plan.Max(p => p[0]), plan.Max(p => p[1]), z1],
        };
    }

    [Fact]
    public void FinishWallEmbeddedInTheHost_IsItsOwnBoxInTheHostFrame()
    {
        var host = WithBody(1, [0, 0, 0], [10, 0, 0], 0.5, 0, 3);
        var finish = WithBody(2, [2, 0.5, 0], [6, 0.5, 0], 0.1, 0, 3);

        Assert.True(IfcExporter.TryBuildCutPairOpening(host, finish, out var opening));

        Assert.Equal(1.0, opening.AxisX, 12);
        Assert.Equal(0.0, opening.AxisY, 12);
        Assert.Equal(4.0, opening.CentreXFt, 12);
        Assert.Equal(0.5, opening.CentreYFt, 12);
        Assert.Equal(4.0, opening.WidthFt, 12);   // along the host
        Assert.Equal(0.2, opening.DepthFt, 12);   // its own thickness: shallower than the 1.0 width of the host, a Recess
        Assert.Equal(0.0, opening.BaseZFt, 12);
        Assert.Equal(3.0, opening.HeightFt, 12);
    }

    [Fact]
    public void RotatedHost_BoxIsInItsOwnFrame()
    {
        var host = WithBody(1, [0, 0, 0], [0, 10, 0], 0.5, 0, 3);
        var finish = WithBody(2, [0.5, 2, 0], [0.5, 6, 0], 0.1, 0, 3);

        Assert.True(IfcExporter.TryBuildCutPairOpening(host, finish, out var opening));

        Assert.Equal(0.0, opening.AxisX, 12);
        Assert.Equal(1.0, opening.AxisY, 12);
        Assert.Equal(0.5, opening.CentreXFt, 12);
        Assert.Equal(4.0, opening.CentreYFt, 12);
        Assert.Equal(4.0, opening.WidthFt, 12);
        Assert.Equal(0.2, opening.DepthFt, 12);
    }

    [Theory]
    [InlineData(2.0, 6.0, 0.6, 0.0, 3.0)]    // beside the host: its face only touches the host's
    [InlineData(2.0, 6.0, 0.5, 3.0, 4.0)]    // above the host
    [InlineData(11.0, 15.0, 0.5, 0.0, 3.0)]  // beyond its end
    public void CutterWhoseBoxOnlyTouchesTheHostsBox_CutsNothing(double x0, double x1, double y, double z0, double z1)
    {
        var host = WithBody(1, [0, 0, 0], [10, 0, 0], 0.5, 0, 3);
        var cutter = WithBody(2, [x0, y, 0], [x1, y, 0], 0.1, z0, z1);

        Assert.False(IfcExporter.CutterBoxIntersectsHostCurrent(host, cutter));
    }

    [Fact]
    public void CutterWhoseBoxOverlapsTheHostsBox_CutsEvenWhenTheBodiesAreApart()
    {
        // Two parallel diagonal walls 1.4 ft apart: the bodies never meet, but the world boxes intersect, and the expected export
        // keeps every joined cutter whose box does (sample project A: a finish wall 1.7 m from its structural wall opens it).
        var host = WithBody(1, [0, 0, 0], [10, 10, 0], 0.5, 0, 3);
        var cutter = WithBody(2, [2 + 1.4, 2 - 1.4, 0], [6 + 1.4, 6 - 1.4, 0], 0.1, 0, 3);

        Assert.True(IfcExporter.CutterBoxIntersectsHostCurrent(host, cutter));
        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        Assert.Equal(0.2, opening.DepthFt, 9);

        // The boxes of a missing or non-finite extent never intersect.
        Assert.False(IfcExporter.CutterBoxIntersectsHostCurrent(host, new Element { Id = 3 }));
        Assert.False(IfcExporter.CutterBoxIntersectsHostCurrent(host, new Element { Id = 4, BoundingBox = [0, 0, 0, double.NaN, 1, 1] }));
    }

    [Fact]
    public void CutterBeyondTheHostsSides_KeepsItsWholeBody()
    {
        // A slab-like cutter far larger than the wall: the expected export writes the whole solid as the opening.
        var host = WithBody(1, [0, 0, 0], [10, 0, 0], 0.5, 0, 3);
        var slab = WithBody(2, [-20, 0, 0], [30, 0, 0], 20, 1, 1.5);

        Assert.True(IfcExporter.TryBuildCutPairOpening(host, slab, out var opening));

        Assert.Equal(50.0, opening.WidthFt, 12);
        Assert.Equal(40.0, opening.DepthFt, 12);
        Assert.Equal(1.0, opening.BaseZFt, 12);
        Assert.Equal(0.5, opening.HeightFt, 12);
    }

    [Fact]
    public void HostWithoutAStraightLocation_CutsNothing()
    {
        var host = WithBody(1, [0, 0, 0], [10, 0, 0], 0.5, 0, 3);
        var finish = WithBody(2, [2, 0.5, 0], [6, 0.5, 0], 0.1, 0, 3);
        host.Location = new LocationCurve { CurveType = "Arc", StartPoint = [0, 0, 0], EndPoint = [10, 0, 0], ArcRadius = 8 };
        Assert.False(IfcExporter.TryBuildCutPairOpening(host, finish, out _));
        host.Location = null;
        Assert.False(IfcExporter.TryBuildCutPairOpening(host, finish, out _));
    }

    [Fact]
    public void CutterWithoutABody_UsesItsBoundingBox()
    {
        var host = WithBody(1, [0, 0, 0], [10, 0, 0], 0.5, 0, 3);
        var cutter = new Element { Id = 2, BoundingBox = [2, 0.4, 0, 6, 0.6, 3] };

        Assert.True(IfcExporter.TryBuildCutPairOpening(host, cutter, out var opening));
        Assert.Equal(0.2, opening.DepthFt, 12);

        Assert.False(IfcExporter.TryBuildCutPairOpening(host, new Element { Id = 3 }, out _));
    }


    private static Element Wall(int id, double[] start, double[] end, double halfWidth, params Parameter[] extra) =>
        Wall(WithBody(id, start, end, halfWidth, 0, 3), halfWidth, extra);

    private static Element Wall(Element wall, double halfWidth, params Parameter[] extra)
    {
        int id = wall.Id;
        wall.TypeId = 101;
        wall.Name = $"Wall {id}";
        wall.CategoryId = -2000011;
        wall.Category = "OST_Walls";
        wall.References = [PhaseId];
        wall.Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.Integer, Value = PhaseId },
            new Parameter { Id = -1001000, StorageType = StorageType.Double, Value = halfWidth * 2 },
            .. extra,
        ];
        wall.SerializedExportId = Guid.NewGuid();
        wall.BoundingBox = wall.Geometry!.Faces!.SelectMany(f => f.OuterVertices!)
            .Aggregate(new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue },
                (b, p) => [Math.Min(b[0], p[0]), Math.Min(b[1], p[1]), Math.Min(b[2], p[2]),
                    Math.Max(b[3], p[0]), Math.Max(b[4], p[1]), Math.Max(b[5], p[2])]);
        wall.Geometry.BoundingBox = wall.BoundingBox;
        wall.Geometry.BrepSolids = [new GeoBrepSolid { FaceIndices = Enumerable.Range(0, wall.Geometry.Faces!.Count).ToList(), IsClosed = true }];
        return wall;
    }

    private static string Export(IfcProfile version, params Element[] elements)
    {
        var info = new RevitFileInfo();
        info.Elements.AddRange(elements);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-cutpair-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static Element[] FinishWallInStructuralWall()
    {
        var host = Wall(100, [0, 0, 0], [10, 0, 0], 0.5);
        var finish = Wall(200, [2, 0.5, 0], [6, 0.5, 0], 0.1);
        host.CuttingElementIds = [];
        host.CutOutSteps = [(977, 200, false)];
        return [host, finish];
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Current_SlabPairOpeningReusesItsCuttersResolvedReference(bool explicitReference)
    {
        var host = Wall(100, [0, 0, 0], [10, 0, 0], 0.5);
        var slab = Wall(200, [2, 0, 0], [6, 0, 0], 2);
        host.CuttingElementIds = [];
        host.CutOutSteps = [(977, slab.Id, false)];
        slab.CategoryId = -2000032;
        slab.Category = "OST_Floors";
        slab.Name = "Borrowed floor name";
        slab.ValidatedFloorTypeReferenceName = "Own floor type";
        if (explicitReference)
        {
            slab.Parameters.Add(new Parameter { Id = 9001, StorageType = StorageType.String,
                Definition = new Definition { Id = 9001, Name = "Reference" }, Value = "Explicit floor reference" });
            slab.ValidatedFloorTypeReferenceName = "Explicit floor reference";
            slab.ValidatedFloorTypeReferenceIsExplicit = true;
        }
        string expected = explicitReference ? "Explicit floor reference" : "Own floor type";
        string ifc = Export(IfcProfile.Current, host, slab);
        Assert.Single(Regex.Matches(ifc, "=IFCOPENINGELEMENT\\("));
        var value = Assert.Single(Regex.Matches(ifc,
            $@"#(\d+)=IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('{expected}'\),\$\);"));
        var common = Assert.Single(Regex.Matches(ifc,
            @"=IFCPROPERTYSET\([^;\r\n]*,'Pset_OpeningElementCommon',(?:\$|''),\(#(\d+)\)\);"));
        Assert.Equal(value.Groups[1].Value, common.Groups[1].Value);
    }

    [Fact]
    public void Current_OpensTheStructuralWallForTheFinishWallThatCutsIt()
    {
        string ifc = Export(IfcProfile.Current, FinishWallInStructuralWall());

        Assert.Equal(2, Regex.Matches(ifc, "=IFCWALLSTANDARDCASE\\(").Count);
        var opening = Assert.Single(Regex.Matches(ifc, @"#(\d+)=IFCOPENINGELEMENT\(([^\r\n]*)\);"));
        // The finish wall is thinner (0.2 ft) than the 1-foot host: a Recess named after the host and tagged with it.
        Assert.Contains(",'Recess',", opening.Groups[2].Value);
        Assert.Contains("Wall 100:100'", opening.Groups[2].Value);
        Assert.EndsWith(",'100'", opening.Groups[2].Value);
        var voids = Assert.Single(Regex.Matches(ifc, @"IFCRELVOIDSELEMENT\([^\r\n]*,#(\d+),#(\d+)\);"));
        Assert.Equal(opening.Groups[1].Value, voids.Groups[2].Value);
    }

    [Fact]
    public void Current_PairStepThatEnclosesNoVolumeOpensNothing()
    {
        var elements = FinishWallInStructuralWall();
        elements[1] = Wall(200, [2, 0.6, 0], [6, 0.6, 0], 0.1);
        elements[0].CutOutSteps = [(977, 200, false)];

        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, elements));
    }

    [Fact]
    public void Current_FinishWallApartFromTheHost_StillOpensIt()
    {
        // sample project A: the finish wall of a Join Geometry pair need not touch its structural wall, only its box meets the host's.
        var host = Wall(100, [0, 0, 0], [10, 10, 0], 0.5);
        var finish = Wall(200, [3.4, 0.6, 0], [7.4, 4.6, 0], 0.1);
        host.CuttingElementIds = [];
        host.CutOutSteps = [(977, 200, false)];

        string ifc = Export(IfcProfile.Current, host, finish);

        var opening = Assert.Single(Regex.Matches(ifc, @"#(\d+)=IFCOPENINGELEMENT\(([^\r\n]*)\);"));
        Assert.Contains(",'Recess',", opening.Groups[2].Value);
    }

    [Fact]
    public void Current_CutterWithoutAnExportedProduct_OpensNothing()
    {
        // A generic model with no body is not a product of the export; the expected export hands such cutters no opening here.
        var elements = FinishWallInStructuralWall();
        elements[1].CategoryId = -2000151;
        elements[1].Category = "OST_GenericModel";
        elements[1].Geometry = null;

        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, elements));
    }

    [Fact]
    public void Current_StandardCaseWallThatIsNoThickenedAxisExtrusion_GetsNoPairOpening()
    {
        // Trapezoid plan: still a vertical extrusion (an IfcWallStandardCase, found by the body exporter), but the two long
        // vertical faces differ in area, so the simple-rectangular-wall test fails and the wall export never reaches the openings step.
        var body = WithPlan(100, [0, 0, 0], [10, 0, 0], 0, 3, [[0, -0.5], [10, -0.5], [9, 0.5], [1, 0.5]]);
        var host = Wall(body, 0.5);
        var finish = Wall(200, [2, 0.5, 0], [6, 0.5, 0], 0.1);
        host.CuttingElementIds = [];
        host.CutOutSteps = [(977, 200, false)];

        Assert.False(IfcExporter.IsAxisExtrusionHostCurrent(host));
        string ifc = Export(IfcProfile.Current, host, finish);

        Assert.Equal(2, Regex.Matches(ifc, "=IFCWALLSTANDARDCASE\\(").Count);
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", ifc);
    }

    private static Element[] GenericCutterInWall(Element host)
    {
        var cutter = WithBody(200, [2, 0, 0], [6, 0, 0], 0.3, 0.5, 2.5);
        cutter.CategoryId = -2000151;
        cutter.Category = "OST_GenericModel";
        cutter.TypeId = 102;
        cutter.Name = "Cutter";
        cutter.SerializedExportId = Guid.NewGuid();
        cutter.SerializedHostId = host.Id;
        cutter.HostId = host.Id;
        cutter.References = [host.Id, PhaseId];
        cutter.Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.Integer, Value = PhaseId }];
        host.CuttingElementIds = [200];
        host.CutOutSteps = [(4137, 200, true)];
        return [host, cutter];
    }

    [Fact]
    public void Current_StandardCaseWallThatIsNoThickenedAxisExtrusion_GetsNoOpeningForAHostedCutterEither()
    {
        // Control: the rectangular wall is opened by its hosted cutter.
        string control = Export(IfcProfile.Current, GenericCutterInWall(Wall(100, [0, 0, 0], [10, 0, 0], 0.5)));
        Assert.Contains("=IFCOPENINGELEMENT(", control);

        // Trapezoid plan: a standard-case wall by its extrusion, but no axis-extrusion host, so no opening beside it.
        var trapezoid = Wall(WithPlan(100, [0, 0, 0], [10, 0, 0], 0, 3, [[0, -0.5], [10, -0.5], [9, 0.5], [1, 0.5]]), 0.5);
        string ifc = Export(IfcProfile.Current, GenericCutterInWall(trapezoid));
        Assert.Contains("=IFCWALLSTANDARDCASE(", ifc);
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", ifc);
        Assert.DoesNotContain("=IFCRELVOIDSELEMENT(", ifc);
    }

    private static Element[] InsertedCutter(Element host, ushort stepClass, int categoryId, int? ownHost)
    {
        var cutter = WithBody(200, [2, 3, 0], [6, 3, 0], 0.3, 0.5, 2.5);
        cutter.CategoryId = categoryId;
        cutter.Category = categoryId == -2000151 ? "OST_GenericModel" : categoryId == -2000014 ? "OST_Windows" : "OST_Doors";
        cutter.TypeId = 102;
        cutter.Name = "Cutter";
        cutter.SerializedExportId = Guid.NewGuid();
        cutter.SerializedHostId = ownHost;
        cutter.References = [PhaseId];
        cutter.Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.Integer, Value = PhaseId }];
        host.CutOutSteps = [(stepClass, 200, true)];
        return [host, cutter];
    }

    [Theory]
    [InlineData(4155, -2000151, 999, true)]    // a shadow cutout of any family, product or not, opens the wall
    [InlineData(4155, -2000023, 999, true)]
    [InlineData(4137, -2000151, 999, true)]    // a cutout of a generic model does
    [InlineData(4137, -2000023, 999, true)]    // so does a door hosted by another wall: an ordinary solid opening
    [InlineData(4137, -2000014, 999, true)]    // and a window
    public void Current_InsertedElementStep_OpensTheWallWhenNoOtherWriterHas(ushort stepClass, int categoryId, int ownHost, bool opens)
    {
        // The cutter stands 3 ft beside the wall and belongs to another wall: only the step names this wall.
        string ifc = Export(IfcProfile.Current, InsertedCutter(Wall(100, [0, 0, 0], [10, 0, 0], 0.5), stepClass, categoryId, ownHost));

        Assert.Equal(opens, ifc.Contains("=IFCOPENINGELEMENT("));
        if (opens)
        {
            var opening = Assert.Single(Regex.Matches(ifc, @"#(\d+)=IFCOPENINGELEMENT\(([^
]*)\);"));
            Assert.EndsWith(",'100'", opening.Groups[2].Value); // tagged with its host, like the pair openings
        }
    }

    [Theory]
    [InlineData(-2000023)]
    [InlineData(-2000014)]
    public void Current_DoorOrWindowOfTheWallsOwn_IsOpenedOnceByTheDoorWindowWriter_NotAgainByTheStep(int categoryId)
    {
        // The opening rule sends the wall's own insert to the delayed (filled) opening: one opening.
        string ifc = Export(IfcProfile.Current, InsertedCutter(Wall(100, [0, 0, 0], [10, 0, 0], 0.5), 4137, categoryId, 100));

        Assert.Single(Regex.Matches(ifc, "=IFCOPENINGELEMENT\\("));
    }

    [Fact]
    public void Current_InsertedElementStep_OpensNothingOnAWallThatIsNoAxisExtrusion()
    {
        var trapezoid = Wall(WithPlan(100, [0, 0, 0], [10, 0, 0], 0, 3, [[0, -0.5], [10, -0.5], [9, 0.5], [1, 0.5]]), 0.5);
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, InsertedCutter(trapezoid, 4155, -2000151, 999)));
    }

    [Fact]
    public void IsAxisExtrusionHostCurrent_NeedsAStraightWallWithASimpleRectangularBody()
    {
        var wall = Wall(100, [0, 0, 0], [10, 0, 0], 0.5);
        Assert.True(IfcExporter.IsAxisExtrusionHostCurrent(wall));

        wall.Location = new LocationCurve { CurveType = "Arc", StartPoint = [0, 0, 0], EndPoint = [10, 0, 0], ArcRadius = 8 };
        Assert.False(IfcExporter.IsAxisExtrusionHostCurrent(wall));

        var withoutBody = Wall(101, [0, 0, 0], [10, 0, 0], 0.5);
        withoutBody.Geometry = null;
        Assert.False(IfcExporter.IsAxisExtrusionHostCurrent(withoutBody));
    }

    [Fact]
    public void Current_AnEmbeddedWallStepOpensTheHostOnlyForACurtainWall_AndARoofPairDoesNot()
    {
        // WallEmbedWallGStep: a basic wall embedded this way is no pair solid (sample project A: 4 of 4 not opened) ...
        var embedded = FinishWallInStructuralWall();
        embedded[0].CutOutSteps = [(4138, 200, true)];
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, embedded));

        // ... a curtain wall (a wall with a placed panel) is, wherever its box lies.
        var curtain = FinishWallInStructuralWall();
        curtain[0].CutOutSteps = [(4138, 200, true)];
        curtain[1].Location = new LocationCurve { CurveType = "Line", StartPoint = [2, 5, 0], EndPoint = [6, 5, 0] };
        curtain[1].BoundingBox = [2, 4.9, 0, 6, 5.1, 3];
        string ifc = Export(IfcProfile.Current, [.. curtain, new Element { Id = 300, CategoryId = -2000171, References = [200] }]);
        Assert.Contains("=IFCCURTAINWALL(", ifc);
        Assert.Contains("=IFCOPENINGELEMENT(", ifc);

        // The expected export's pair cutters are walls and slabs.
        var roof = FinishWallInStructuralWall();
        roof[1].CategoryId = -2000035;
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, roof));
    }

    [Fact]
    public void Current_OnlyAStandardCaseWallIsOpened()
    {
        // A wall whose body is no extrusion is an IfcWall; its openings are cut into the body, not written.
        var elements = FinishWallInStructuralWall();
        elements[0].Geometry!.Faces![2].Normal = [0, -Math.Sqrt(0.99), 0.1];

        string ifc = Export(IfcProfile.Current, elements);

        Assert.Contains("=IFCWALL(", ifc);
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", ifc);
    }

    [Fact]
    public void Legacy_WritesNoOpeningForAPairStep()
    {
        string ifc = Export(IfcProfile.Legacy, FinishWallInStructuralWall());

        Assert.DoesNotContain("=IFCOPENINGELEMENT(", ifc);
        Assert.DoesNotContain("=IFCRELVOIDSELEMENT(", ifc);
    }
}
