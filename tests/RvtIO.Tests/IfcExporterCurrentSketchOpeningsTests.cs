using System.Globalization;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile cuts an opening from the wall extrusion for every hole of the wall's profile sketch, even when the body is a plain box. The legacy profile writes none.</summary>
public class IfcExporterCurrentSketchOpeningsTests
{
    private const int PhaseId = 99, WallId = 100, OwnerId = 300;

    private static readonly double[][] Outer = [[0, 0, 0], [10, 0, 0], [10, 0, 3], [0, 0, 3]];
    private static readonly double[][] HoleA = [[2, 0, 1], [4, 0, 1], [4, 0, 2], [2, 0, 2]];
    private static readonly double[][] HoleB = [[6, 0, 1], [7, 0, 1], [7, 0, 2], [6, 0, 2]];

    [Fact]
    public void SketchHoles_AreTheLoopsInsideAnOddNumberOfOthers()
    {
        var holes = IfcExporter.SketchHolesCurrent([Outer, HoleA, HoleB], 1, 0);

        Assert.Equal(new[] { 1, 2 }, holes.Select(hole => hole.LoopIndex));
        var hole = holes[0];
        Assert.Equal((2.0, 4.0, 1.0, 2.0, 0.0), (hole.U0, hole.U1, hole.V0, hole.V1, hole.Across));
        Assert.True(hole.Rectangular);
    }

    [Fact]
    public void SketchHoles_AreMeasuredAlongTheWallAxis()
    {
        // The same sketch moved by (0.8, 0.6) onto a wall rotated by the 3-4-5 triangle: u runs along (0.6, 0.8) and
        // across along (-0.8, 0.6), so a point (x, y) has u = x + 0.96 and across = y - 0.28.
        static double[][] Rotate(double[][] loop) => loop.Select(p => new[] { 0.6 * p[0] - 0.8 * p[1] + 0.8, 0.8 * p[0] + 0.6 * p[1] + 0.6, p[2] }).ToArray();

        var hole = Assert.Single(IfcExporter.SketchHolesCurrent([Rotate(Outer), Rotate(HoleA)], 0.6, 0.8));

        Assert.Equal((2.96, 4.96, 1.0, 2.0, -0.28), (Math.Round(hole.U0, 9), Math.Round(hole.U1, 9), hole.V0, hole.V1, Math.Round(hole.Across, 9)));
    }

    [Fact]
    public void SketchHoles_NeedLoopsInOnePlane_AndFlagNonRectangles()
    {
        double[][] shifted = HoleA.Select(p => new[] { p[0], 0.5, p[2] }).ToArray();
        Assert.Empty(IfcExporter.SketchHolesCurrent([Outer, shifted], 1, 0));

        Assert.Empty(IfcExporter.SketchHolesCurrent([Outer], 1, 0));
        Assert.Empty(IfcExporter.SketchHolesCurrent([Outer, [[2, 0, 1], [4, 0, 1]]], 1, 0));

        double[][] triangle = [[2, 0, 1], [4, 0, 1], [3, 0, 2]];
        Assert.False(Assert.Single(IfcExporter.SketchHolesCurrent([Outer, triangle], 1, 0)).Rectangular);

        // A loop closed by repeating its first point is the same loop, even when that point differs in the last digits
        // (the walk of BuildSketchLoops ends on the start vertex of the last line).
        double[][] closed = [.. HoleA, HoleA[0]];
        Assert.True(Assert.Single(IfcExporter.SketchHolesCurrent([Outer, closed], 1, 0)).Rectangular);
        double[][] drifting = [.. HoleA, [HoleA[0][0] + 1e-13, HoleA[0][1], HoleA[0][2] - 1e-13]];
        Assert.True(Assert.Single(IfcExporter.SketchHolesCurrent([Outer, drifting], 1, 0)).Rectangular);
    }

    [Fact]
    public void SketchHoles_NeedOneRectangularOutline()
    {
        double[][] lShape = [[0, 0, 0], [10, 0, 0], [10, 0, 1.5], [5, 0, 1.5], [5, 0, 3], [0, 0, 3]];
        Assert.Empty(IfcExporter.SketchHolesCurrent([lShape, HoleA], 1, 0));

        // Two outlines (a loop outside every other, or an island inside a hole: inside two loops) are no single wall profile.
        double[][] farIsland = [[20, 0, 0], [22, 0, 0], [22, 0, 1], [20, 0, 1]];
        Assert.Empty(IfcExporter.SketchHolesCurrent([Outer, HoleA, farIsland], 1, 0));
        double[][] island = [[2.5, 0, 1.25], [3.5, 0, 1.25], [3.5, 0, 1.75], [2.5, 0, 1.75]];
        Assert.Empty(IfcExporter.SketchHolesCurrent([Outer, HoleA, island], 1, 0));
    }

    private static Element Wall(int id)
    {
        double[] start = [0, 0, 0], end = [10, 0, 0];
        double[][] plan = [[0, -0.5], [10, -0.5], [10, 0.5], [0, 0.5]];
        var faces = new List<GeoFace>
        {
            new() { Normal = [0, 0, -1], Origin = [0, -0.5, 0], OuterVertices = plan.AsEnumerable().Reverse().Select(p => new[] { p[0], p[1], 0.0 }).ToList() },
            new() { Normal = [0, 0, 1], Origin = [0, -0.5, 3], OuterVertices = plan.Select(p => new[] { p[0], p[1], 3.0 }).ToList() },
        };
        for (int i = 0; i < 4; i++)
        {
            var p = plan[i]; var q = plan[(i + 1) % 4];
            double sx = q[0] - p[0], sy = q[1] - p[1], length = Math.Sqrt(sx * sx + sy * sy);
            faces.Add(new GeoFace
            {
                Normal = [sy / length, -sx / length, 0],
                OuterVertices = [[p[0], p[1], 0], [q[0], q[1], 0], [q[0], q[1], 3], [p[0], p[1], 3]],
            });
        }
        double[] box = [0, -0.5, 0, 10, 0.5, 3];
        return new Element
        {
            Id = id, TypeId = 101, Name = $"Wall {id}", CategoryId = -2000011, Category = "OST_Walls", References = [PhaseId],
            Location = new LocationCurve { CurveType = "Line", StartPoint = start, EndPoint = end },
            Geometry = new GeometryData
            {
                Faces = faces, BoundingBox = box,
                BrepSolids = [new GeoBrepSolid { FaceIndices = Enumerable.Range(0, faces.Count).ToList(), IsClosed = true }],
            },
            BoundingBox = box, SerializedExportId = Guid.NewGuid(),
            Parameters =
            [
                new Parameter { Id = -1012100, StorageType = StorageType.Integer, Value = PhaseId },
                new Parameter { Id = -1001000, StorageType = StorageType.Double, Value = 1.0 },
            ],
        };
    }

    /// <summary>The wall with its profile sketch: the owner, the loops and a line for every side, each referencing the wall.</summary>
    private static List<Element> WallWithSketch(params double[][][] loops)
    {
        var elements = new List<Element> { Wall(WallId) };
        var owner = new Element { Id = OwnerId, SketchLoops = loops.Select(loop => loop.ToList()).ToList(), References = [WallId] };
        elements.Add(owner);
        int lineId = 0;
        foreach (var loop in loops)
        {
            int first = lineId + 1;
            for (int i = 0; i < loop.Length; i++)
            {
                lineId++;
                var line = new Element
                {
                    Id = lineId, CategoryId = -2000045, HostId = OwnerId, References = [WallId], SketchLoopKey = first,
                    Location = new LocationCurve { CurveType = "Line", StartPoint = loop[i], EndPoint = loop[(i + 1) % loop.Length] },
                };
                elements.Add(line);
                owner.References.Add(lineId);
            }
        }
        return elements;
    }

    private static string Export(IfcProfile version, IEnumerable<Element> elements)
    {
        var info = new RevitFileInfo();
        info.Elements.AddRange(elements);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-sketch-{Guid.NewGuid():N}.ifc");
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

    private static List<(string Name, string ObjectType, string Tag)> Openings(string ifc) =>
        Regex.Matches(ifc, @"=IFCOPENINGELEMENT\('([^']*)',#\d+,'([^']*)',[^,]*,'([^']*)',[^\r\n]*,'([^']*)'\);")
            .Select(m => (m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value)).ToList();

    [Fact]
    public void Current_WritesAnOpeningForEveryHoleOfTheWallsProfile_NumberedFromTheLastLoop()
    {
        string ifc = Export(IfcProfile.Current, WallWithSketch(Outer, HoleA, HoleB));

        // Hole B's lines come last (smallest line 9 against 5), so it is opening 1.
        var openings = Openings(ifc);
        Assert.Equal(new[] { "OST_Walls:Wall 100:100:1", "OST_Walls:Wall 100:100:2" }, openings.Select(o => o.Name).Order());
        Assert.All(openings, o => Assert.Equal(("Opening", "100"), (o.ObjectType, o.Tag)));
        Assert.Equal(2, Regex.Matches(ifc, "=IFCRELVOIDSELEMENT\\(").Count);
        // The profile: the hole's width along the wall (2 ft and 1 ft) by the 1 ft width of the wall, in millimetres.
        var profiles = Regex.Matches(ifc, @"=IFCRECTANGLEPROFILEDEF\(\.AREA\.,\$,#\d+,([^,]+),([^)]+)\);")
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .OrderBy(p => p.Item1).ToList();
        Assert.Equal(2, profiles.Count);
        Assert.Equal((304.8, 304.8), (Math.Round(profiles[0].Item1, 6), Math.Round(profiles[0].Item2, 6)));
        Assert.Equal((609.6, 304.8), (Math.Round(profiles[1].Item1, 6), Math.Round(profiles[1].Item2, 6)));
    }

    [Fact]
    public void Legacy_WritesNoSketchOpening()
    {
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Legacy, WallWithSketch(Outer, HoleA)));
    }

    [Fact]
    public void Current_SketchWithoutAHole_OrNotOwnedByTheWall_OpensNothing()
    {
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, WallWithSketch(Outer)));

        // The owner of the sketch does not reference the wall: some other element's sketch.
        var strangers = WallWithSketch(Outer, HoleA);
        strangers.Single(e => e.Id == OwnerId).References!.Remove(WallId);
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, strangers));

        // A line of the hole that does not reference the wall (or does twice) is not the wall's profile.
        foreach (int[] references in new[] { new int[0], [WallId, WallId] })
        {
            var foreign = WallWithSketch(Outer, HoleA);
            foreign.Single(e => e.Id == 6).References = [.. references];
            Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, foreign));
        }
    }

    [Fact]
    public void Current_HoleThatIsNoRectangle_OrWallThatIsNoAxisExtrusion_OpensNothing()
    {
        // A triangular hole has three lines: the rectangle writer leaves it.
        double[][] triangle = [[2, 0, 1], [4, 0, 1], [3, 0, 2]];
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, WallWithSketch(Outer, triangle)));

        var arc = WallWithSketch(Outer, HoleA);
        arc[0].Location = new LocationCurve { CurveType = "Arc", StartPoint = [0, 0, 0], EndPoint = [10, 0, 0], ArcRadius = 8 };
        Assert.DoesNotContain("=IFCOPENINGELEMENT(", Export(IfcProfile.Current, arc));
    }
}
