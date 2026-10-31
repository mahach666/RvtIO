using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile wall bodies: the frame at the start of the axis, the axis as an IfcTrimmedCurve over an IfcLine and the body as an IfcExtrudedAreaSolid over the unnamed rectangle of the thickened axis.</summary>
public class IfcExporterCurrentWallExtrusionTests
{
    private static void Near(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++) Assert.True(Math.Abs(expected[i] - actual[i]) < 1e-9, $"[{i}] {expected[i]} vs {actual[i]}");
    }

    internal sealed class Fixture
    {
        public StepWriter W { get; } = new();
        public int Origin, StoreyPlacement, BodyContext, AxisContext;

        public Fixture()
        {
            Origin = W.Add("IFCCARTESIANPOINT", "(0.,0.,0.)");
            int identity = W.Add("IFCAXIS2PLACEMENT3D", $"#{Origin},$,$");
            StoreyPlacement = W.Add("IFCLOCALPLACEMENT", $"$,#{identity}");
            BodyContext = W.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT", "'Body'");
            AxisContext = W.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT", "'Axis'");
        }

        public int Point2D(double x, double y) => W.Add("IFCCARTESIANPOINT", $"({R(x)},{R(y)})");
        public int Point3D(double x, double y, double z) => W.Add("IFCCARTESIANPOINT", $"({R(x)},{R(y)},{R(z)})");
        public int Direction3D(double x, double y, double z) => W.Add("IFCDIRECTION", $"({R(x)},{R(y)},{R(z)})");

        /// <summary>A standard-case wall in the historical form of the writer: centre placement, polyline axis, polyline profile.</summary>
        public int StandardCaseWall(double x, double y, double z, double alongX, double alongY, double length, double width,
            double height, (double X, double Y)[]? profile = null)
        {
            int location = Point3D(x, y, z);
            int direction = Direction3D(alongX, alongY, 0);
            int frame = W.Add("IFCAXIS2PLACEMENT3D", $"#{location},$,#{direction}");
            int placement = W.Add("IFCLOCALPLACEMENT", $"#{StoreyPlacement},#{frame}");

            int axisStart = Point2D(-length / 2, 0), axisEnd = Point2D(length / 2, 0);
            int axisCurve = W.Add("IFCPOLYLINE", $"(#{axisStart},#{axisEnd})");
            int axisRepresentation = W.Add("IFCSHAPEREPRESENTATION", $"#{AxisContext},'Axis','Curve2D',(#{axisCurve})");

            profile ??= [(-length / 2, -width / 2), (length / 2, -width / 2), (length / 2, width / 2), (-length / 2, width / 2)];
            var corners = profile.Select(p => Point2D(p.X, p.Y)).ToList();
            int outline = W.Add("IFCPOLYLINE", $"({string.Join(",", corners.Append(corners[0]).Select(id => "#" + id))})");
            int profileId = W.Add("IFCARBITRARYCLOSEDPROFILEDEF", $".AREA.,$,#{outline}");
            int positionOrigin = W.Add("IFCCARTESIANPOINT", "(0.,0.,0.)");
            int position = W.Add("IFCAXIS2PLACEMENT3D", $"#{positionOrigin},$,$");
            int up = W.Add("IFCDIRECTION", "(0.,0.,1.)");
            int solid = W.Add("IFCEXTRUDEDAREASOLID", $"#{profileId},#{position},#{up},{R(height)}");
            int bodyRepresentation = W.Add("IFCSHAPEREPRESENTATION", $"#{BodyContext},'Body','SweptSolid',(#{solid})");

            int shape = W.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{axisRepresentation},#{bodyRepresentation})");
            return W.Add("IFCWALLSTANDARDCASE", $"'guid',#1,'Wall',$,'Wall',#{placement},#{shape},'10'");
        }

        /// <summary>A wall (IfcWall) whose body is one faceted Brep prism over the footprint (metres), with the historical identity placement.</summary>
        public int BrepWall((double X, double Y)[] footprint, double z0, double z1, out List<int> pointIds)
        {
            var collected = new List<int>();
            int Ring(IEnumerable<(double X, double Y, double Z)> ring)
            {
                var ids = ring.Select(p => Point3D(p.X, p.Y, p.Z)).ToList();
                collected.AddRange(ids);
                int loop = W.Add("IFCPOLYLOOP", $"({string.Join(",", ids.Select(id => "#" + id))})");
                int bound = W.Add("IFCFACEOUTERBOUND", $"#{loop},.T.");
                return W.Add("IFCFACE", $"(#{bound})");
            }
            var faces = new List<int>
            {
                Ring(footprint.Select(p => (p.X, p.Y, z0)).AsEnumerable().Reverse()),
                Ring(footprint.Select(p => (p.X, p.Y, z1))),
            };
            for (int i = 0; i < footprint.Length; i++)
            {
                var a = footprint[i]; var b = footprint[(i + 1) % footprint.Length];
                faces.Add(Ring([(a.X, a.Y, z0), (b.X, b.Y, z0), (b.X, b.Y, z1), (a.X, a.Y, z1)]));
            }
            pointIds = collected;
            int shell = W.Add("IFCCLOSEDSHELL", $"({string.Join(",", faces.Select(id => "#" + id))})");
            int brep = W.Add("IFCFACETEDBREP", $"#{shell}");
            int bodyRepresentation = W.Add("IFCSHAPEREPRESENTATION", $"#{BodyContext},'Body','Brep',(#{brep})");
            int shape = W.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{bodyRepresentation})");
            int identity = W.Add("IFCAXIS2PLACEMENT3D", $"#{Origin},$,$");
            int placement = W.Add("IFCLOCALPLACEMENT", $"#{StoreyPlacement},#{identity}");
            return W.Add("IFCWALL", $"'guid',#1,'Wall',$,'Wall',#{placement},#{shape},'11'");
        }

        private static string R(double v) => StepWriter.Real(v);

        public (string Type, string Args) Find(int id)
        {
            for (int i = 0; i < W.EntityCount; i++)
                if (W.EntityAt(i).Id == id) return (W.EntityAt(i).Type, W.EntityAt(i).Args);
            throw new KeyNotFoundException($"#{id}");
        }

        public bool Exists(int id) => Enumerable.Range(0, W.EntityCount).Any(i => W.EntityAt(i).Id == id);

        /// <summary>The numbers of a point or direction entity.</summary>
        public double[] Numbers(string reference)
        {
            var (_, args) = Find(int.Parse(reference.TrimStart('#')));
            return args.Trim('(', ')').Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }

        public string[] Attributes(int id) => IfcExporter.SplitStepArguments(Find(id).Args);
    }

    [Fact]
    public void StandardCase_RectangleWall_IsWrittenInTheFrameAtTheStartOfTheAxis()
    {
        var f = new Fixture();
        // 3.2 m long, 0.2 m thick, 2.7 m high, centred at (1, 2, 0.3), running along +Y
        int wall = f.StandardCaseWall(1, 2, 0.3, 0, 1, 3.2, 0.2, 2.7);

        Assert.Equal(1, new IfcExporter().ApplyCurrentWallExtrusions(f.W));

        var attributes = f.Attributes(wall);
        var placement = f.Attributes(int.Parse(attributes[5][1..]));
        var frame = f.Attributes(int.Parse(placement[1][1..]));
        Near([1.0, 0.4, 0.3], f.Numbers(frame[0]));                  // the centre moved back by half the length
        Near([0.0, 0.0, 1.0], f.Numbers(frame[1]));                    // the Z axis is written
        Near([0.0, 1.0, 0.0], f.Numbers(frame[2]));                    // X along the axis

        var representations = f.Attributes(int.Parse(attributes[6][1..]));
        var ids = representations[2].Trim('(', ')').Split(',').Select(token => int.Parse(token[1..])).ToList();
        var axis = f.Attributes(ids[0]);
        var curve = f.Find(int.Parse(f.Attributes(ids[0])[3].Trim('(', ')')[1..]));
        Assert.Equal("IFCTRIMMEDCURVE", curve.Type);
        var trimmed = IfcExporter.SplitStepArguments(curve.Args);
        Assert.Equal("IFCLINE", f.Find(int.Parse(trimmed[0][1..])).Type);
        Near([3.2, 0.0, 0.0], f.Numbers(trimmed[2].Trim('(', ')')));   // trimmed at the end of the axis
        Assert.Equal(".T.,.CARTESIAN.", $"{trimmed[3]},{trimmed[4]}");
        // the line starts at the shared origin and runs along the shared X direction, one foot long
        var line = f.Attributes(int.Parse(trimmed[0][1..]));
        Assert.Equal(f.Origin, int.Parse(line[0][1..]));
        Assert.Equal(StepWriter.Real(0.3048), f.Attributes(int.Parse(line[1][1..]))[1]);

        var body = f.Attributes(ids[1]);
        var solid = f.Attributes(int.Parse(body[3].Trim('(', ')')[1..]));
        var profile = f.Find(int.Parse(solid[0][1..]));
        Assert.Equal("IFCRECTANGLEPROFILEDEF", profile.Type);
        var profileAttributes = IfcExporter.SplitStepArguments(profile.Args);
        Assert.Equal(".AREA.", profileAttributes[0]);
        Assert.Equal("''", profileAttributes[1]);
        Assert.Equal("3.2,0.2", $"{profileAttributes[3]},{profileAttributes[4]}");
        var profilePlacement = f.Attributes(int.Parse(profileAttributes[2][1..]));
        Near([1.6, 0.0], f.Numbers(profilePlacement[0]));              // about the point halfway along
        Near([-1.0, 0.0], f.Numbers(profilePlacement[1]));               // RefDirection (-1, 0)
        var position = f.Attributes(int.Parse(solid[1][1..]));
        Assert.Equal(f.Origin, int.Parse(position[0][1..]));
        Assert.Equal("$,$", $"{position[1]},{position[2]}");
        Near([0.0, 0.0, 1.0], f.Numbers(solid[2]));
        Assert.Equal("2.7", solid[3]);
    }

    [Fact]
    public void StandardCase_WallAlongX_HasDefaultAxesAndTheShapeStaysWhereItWas()
    {
        var f = new Fixture();
        int wall = f.StandardCaseWall(5, 1, 0, 1, 0, 4, 0.3, 3);

        Assert.Equal(1, new IfcExporter().ApplyCurrentWallExtrusions(f.W));

        var placement = f.Attributes(int.Parse(f.Attributes(wall)[5][1..]));
        var frame = f.Attributes(int.Parse(placement[1][1..]));
        Near([3.0, 1.0, 0.0], f.Numbers(frame[0]));
        Assert.Equal("$,$", $"{frame[1]},{frame[2]}");                          // createAxis: both axes unset for the default frame
    }

    [Fact]
    public void StandardCase_ShiftedProfile_KeepsItsCentreRelativeToTheAxisStart()
    {
        var f = new Fixture();
        // the body is longer than the axis at its far end: x from -1.6 to 1.9
        int wall = f.StandardCaseWall(0, 0, 0, 1, 0, 3.2, 0.2, 2.7,
            [(-1.6, -0.1), (1.9, -0.1), (1.9, 0.1), (-1.6, 0.1)]);

        Assert.Equal(1, new IfcExporter().ApplyCurrentWallExtrusions(f.W));

        var shape = f.Attributes(int.Parse(f.Attributes(wall)[6][1..]));
        var bodyRep = f.Attributes(int.Parse(shape[2].Trim('(', ')').Split(',')[1][1..]));
        var solid = f.Attributes(int.Parse(bodyRep[3].Trim('(', ')')[1..]));
        var profile = IfcExporter.SplitStepArguments(f.Find(int.Parse(solid[0][1..])).Args);
        Assert.Equal("3.5", profile[3]);
        Near([1.75, 0.0], f.Numbers(f.Attributes(int.Parse(profile[2][1..]))[0]));
    }

    [Fact]
    public void StandardCase_NonRectangularProfile_IsLeftAsItWas()
    {
        var f = new Fixture();
        int wall = f.StandardCaseWall(0, 0, 0, 1, 0, 3.2, 0.2, 2.7,
            [(-1.6, -0.1), (1.6, -0.1), (1.7, 0.1), (-1.6, 0.1)]);
        string before = f.Find(wall).Args;
        int count = f.W.EntityCount;

        Assert.Equal(0, new IfcExporter().ApplyCurrentWallExtrusions(f.W));

        Assert.Equal(before, f.Find(wall).Args);
        Assert.Equal(count, f.W.EntityCount);
    }

    [Fact]
    public void StandardCase_RemovesTheEntitiesNothingReferencesAnyMore()
    {
        var f = new Fixture();
        f.StandardCaseWall(0, 0, 0, 1, 0, 3.2, 0.2, 2.7);
        int before = f.W.EntityCount;

        new IfcExporter().ApplyCurrentWallExtrusions(f.W);

        // 2 axis points, 4 profile points and their closed polyline, the old frame (point, direction and placement), the
        // position origin and the up direction are gone; the shared origin and Z direction replace the last two
        Assert.True(f.W.EntityCount < before + 12, $"{before} -> {f.W.EntityCount}");
        Assert.DoesNotContain(Enumerable.Range(0, f.W.EntityCount), i => f.W.EntityAt(i).Type == "IFCARBITRARYCLOSEDPROFILEDEF");
        Assert.DoesNotContain(Enumerable.Range(0, f.W.EntityCount), i => f.W.EntityAt(i).Type == "IFCPOLYLINE");
    }

    /// <summary>A wall body: a vertical prism (feet) over a counter-clockwise plan polygon, with the plane frame on the horizontal faces.</summary>
    private static GeometryData Prism(double[][] plan, double z0, double z1)
    {
        double[] u = [1, 0, 0], v = [0, 1, 0];
        var faces = new List<GeoFace>
        {
            new() { Origin = [plan[0][0], plan[0][1], z0], Normal = [0, 0, -1], PlaneXAxis = u, PlaneYAxis = v,
                OuterVertices = plan.AsEnumerable().Reverse().Select(p => new[] { p[0], p[1], z0 }).ToList() },
            new() { Origin = [plan[0][0], plan[0][1], z1], Normal = [0, 0, 1], PlaneXAxis = u, PlaneYAxis = v,
                OuterVertices = plan.Select(p => new[] { p[0], p[1], z1 }).ToList() },
        };
        for (int i = 0; i < plan.Length; i++)
        {
            var p = plan[i]; var q = plan[(i + 1) % plan.Length];
            double dx = q[0] - p[0], dy = q[1] - p[1], length = Math.Sqrt(dx * dx + dy * dy);
            faces.Add(new GeoFace { Origin = [p[0], p[1], z0], Normal = [dy / length, -dx / length, 0],
                OuterVertices = [[p[0], p[1], z0], [q[0], q[1], z0], [q[0], q[1], z1], [p[0], p[1], z1]] });
        }
        return new GeometryData { Faces = faces };
    }

    [Fact]
    public void StandardCase_WallThatIsNotAThickenedAxis_TakesItsProfileFromTheLowerEndFaceOfTheBody()
    {
        var f = new Fixture();
        // metres: 0..3 along +X, one end mitred; the body in feet
        double k = 1 / 0.3048;
        double[][] plan = [[0, -0.1], [3, -0.1], [3.2, 0.1], [0, 0.1]];
        var geometry = Prism(plan.Select(p => new[] { p[0] * k, p[1] * k }).ToArray(), 0, 2.7 * k);
        // the historical form of the writer: the axis from -1.5 to 1.5 about (1.5, 0), a profile that is some other polygon
        int wall = f.StandardCaseWall(1.5, 0, 0, 1, 0, 3, 0.2, 2.7, [(-1.5, -0.1), (1.5, -0.1), (1.7, 0.1), (-1.5, 0.1)]);
        var element = new Element
        {
            Id = 30, Geometry = geometry,
            Location = new LocationCurve { CurveType = "Line", StartPoint = [0, 0, 0], EndPoint = [3 * k, 0, 0] },
        };

        int converted = new IfcExporter().ApplyCurrentWallExtrusions(f.W, new Dictionary<int, Element> { [30] = element },
            new Dictionary<int, int> { [wall] = 30 }, f.AxisContext);

        Assert.Equal(1, converted);
        var shape = f.Attributes(int.Parse(f.Attributes(wall)[6][1..]));
        var body = f.Attributes(int.Parse(shape[2].Trim('(', ')').Split(',')[1][1..]));
        var solid = f.Attributes(int.Parse(body[3].Trim('(', ')')[1..]));
        Assert.Equal("2.7", solid[3]);
        var profile = f.Find(int.Parse(solid[0][1..]));
        Assert.Equal("IFCARBITRARYCLOSEDPROFILEDEF", profile.Type);
        Assert.Equal(".AREA.,''", string.Join(",", IfcExporter.SplitStepArguments(profile.Args).Take(2)));
        var curve = f.Find(int.Parse(IfcExporter.SplitStepArguments(profile.Args)[2][1..]));
        var points = curve.Args.Trim('(', ')').Split(',').Select(token => f.Numbers(token)).ToList();
        Assert.Equal(5, points.Count);
        Near([1.65, 0.1], points[0]);
        Near([-1.55, 0.1], points[1]);
        Near([-1.55, -0.1], points[2]);
        Near([1.45, -0.1], points[3]);
        Near(points[0], points[4]);
        var position = f.Attributes(int.Parse(solid[1][1..]));
        Near([1.55, 0.0, 0.0], f.Numbers(position[0]));
        Assert.Equal("$,$", $"{position[1]},{position[2]}");
    }

    private static Element TrimmedWall(int id, double[] startMetres, double[] endMetres) => new()
    {
        Id = id,
        OwnWallTrimmedAxis = [startMetres.Select(v => v / 0.3048).ToArray(), endMetres.Select(v => v / 0.3048).ToArray()],
    };

    [Fact]
    public void StandardCase_TrimmedAxisOfTheWall_BoundsTheAxisTheFrameAndTheRectangle()
    {
        var f = new Fixture();
        int wall = f.StandardCaseWall(1.5, 0, 0, 1, 0, 3, 0.2, 2.7);
        var elements = new Dictionary<int, Element> { [40] = TrimmedWall(40, [0.1, 0, 0], [3.2, 0, 0]) };

        Assert.Equal(1, new IfcExporter().ApplyCurrentWallExtrusions(f.W, elements, new Dictionary<int, int> { [wall] = 40 }, f.AxisContext));

        var attributes = f.Attributes(wall);
        var frame = f.Attributes(int.Parse(f.Attributes(int.Parse(attributes[5][1..]))[1][1..]));
        Near([0.1, 0.0, 0.0], f.Numbers(frame[0]));
        var shape = f.Attributes(int.Parse(attributes[6][1..]));
        var ids = shape[2].Trim('(', ')').Split(',').Select(token => int.Parse(token[1..])).ToList();
        var curve = IfcExporter.SplitStepArguments(f.Find(int.Parse(f.Attributes(ids[0])[3].Trim('(', ')')[1..])).Args);
        Near([3.1, 0.0, 0.0], f.Numbers(curve[2].Trim('(', ')')));
        var body = f.Attributes(ids[1]);
        var solid = f.Attributes(int.Parse(body[3].Trim('(', ')')[1..]));
        var profile = IfcExporter.SplitStepArguments(f.Find(int.Parse(solid[0][1..])).Args);
        Assert.Equal("3.1", profile[3]);
        Near([1.55, 0.0], f.Numbers(f.Attributes(int.Parse(profile[2][1..]))[0]));
    }

    [Fact]
    public void StandardCase_TrimmedAxisAgreeingWithTheWrittenOne_LeavesTheWallAsItIs()
    {
        var plain = new Fixture();
        plain.StandardCaseWall(1.5, 0, 0, 1, 0, 3, 0.2, 2.7);
        new IfcExporter().ApplyCurrentWallExtrusions(plain.W);
        var trimmed = new Fixture();
        int trimmedWall = trimmed.StandardCaseWall(1.5, 0, 0, 1, 0, 3, 0.2, 2.7);
        // 4 micrometres off: below the 1e-5 m the written bound is kept within
        var elements = new Dictionary<int, Element> { [40] = TrimmedWall(40, [0.000004, 0, 0], [3.000004, 0, 0]) };

        new IfcExporter().ApplyCurrentWallExtrusions(trimmed.W, elements, new Dictionary<int, int> { [trimmedWall] = 40 }, trimmed.AxisContext);

        Assert.Equal(plain.W.EntityCount, trimmed.W.EntityCount);
        for (int i = 0; i < plain.W.EntityCount; i++) Assert.Equal(plain.W.EntityAt(i).Args, trimmed.W.EntityAt(i).Args);
    }

    private static Element WallElement(int id, double[] startFeet, double[] endFeet) => new()
    {
        Id = id,
        Location = new LocationCurve { CurveType = "Line", StartPoint = startFeet, EndPoint = endFeet },
    };

    [Fact]
    public void Brep_Wall_GetsTheTrimmedAxisAndABodyInTheFrameOfTheWall()
    {
        var f = new Fixture();
        // a wall 3 m long along +X at y = 3, 0.2 m thick, from z = 0.5 to 3.2; the location line starts at (3, 3) m
        int wall = f.BrepWall([(3, 2.9), (6, 2.9), (6, 3.1), (3, 3.1)], 0.5, 3.2, out var pointIds);
        var elements = new Dictionary<int, Element> { [20] = WallElement(20, [3 / 0.3048, 3 / 0.3048, 0], [6 / 0.3048, 3 / 0.3048, 0]) };

        int converted = new IfcExporter().ApplyCurrentWallExtrusions(f.W, elements, new Dictionary<int, int> { [wall] = 20 }, f.AxisContext);

        Assert.Equal(1, converted);
        var attributes = f.Attributes(wall);
        var frame = f.Attributes(int.Parse(f.Attributes(int.Parse(attributes[5][1..]))[1][1..]));
        Near([3.0, 3.0, 0.5], f.Numbers(frame[0]));
        var shape = f.Attributes(int.Parse(attributes[6][1..]));
        var representations = shape[2].Trim('(', ')').Split(',').Select(token => int.Parse(token[1..])).ToList();
        Assert.Equal(2, representations.Count);
        var axis = f.Attributes(representations[0]);
        Assert.Equal($"#{f.AxisContext},'Axis','Curve2D'", $"{axis[0]},{axis[1]},{axis[2]}");
        var curve = IfcExporter.SplitStepArguments(f.Find(int.Parse(axis[3].Trim('(', ')')[1..])).Args);
        Near([3.0, 0.0, 0.0], f.Numbers(curve[2].Trim('(', ')')));
        // the body: x along the wall from 0 to 3, y across from -0.1 to 0.1, z up from 0
        var local = pointIds.Select(id => f.Numbers("#" + id)).ToList();
        Near([0.0, 3.0], [local.Min(p => p[0]), local.Max(p => p[0])]);
        Near([-0.1, 0.1], [local.Min(p => p[1]), local.Max(p => p[1])]);
        Near([0.0, 2.7], [local.Min(p => p[2]), local.Max(p => p[2])]);
    }

    [Fact]
    public void Brep_MiteredEnd_TrimsTheAxisWhereItMeetsTheOutline()
    {
        var f = new Fixture();
        // the far end is a mitre: the sides reach x = 6.1 and 5.9, the line at y = 3 meets the end face at 6.0
        int wall = f.BrepWall([(3, 2.9), (5.9, 2.9), (6.1, 3.1), (3, 3.1)], 0, 3, out var pointIds);
        var elements = new Dictionary<int, Element> { [20] = WallElement(20, [3 / 0.3048, 3 / 0.3048, 0], [6 / 0.3048, 3 / 0.3048, 0]) };

        Assert.Equal(1, new IfcExporter().ApplyCurrentWallExtrusions(f.W, elements, new Dictionary<int, int> { [wall] = 20 }, f.AxisContext));

        var shape = f.Attributes(int.Parse(f.Attributes(wall)[6][1..]));
        var axis = f.Attributes(int.Parse(shape[2].Trim('(', ')').Split(',')[0][1..]));
        var curve = IfcExporter.SplitStepArguments(f.Find(int.Parse(axis[3].Trim('(', ')')[1..])).Args);
        Near([3.0], [f.Numbers(curve[2].Trim('(', ')'))[0]]);
    }

    [Fact]
    public void Brep_TrimmedAxisOfTheWall_ReplacesTheMeetingOfTheLineWithTheOutline()
    {
        var f = new Fixture();
        // the far end is a mitre reaching x = 6.1 and 5.9: the line meets the end face at 6.0, the vertices of the side faces reach 6.1
        int wall = f.BrepWall([(3, 2.9), (5.9, 2.9), (6.1, 3.1), (3, 3.1)], 0, 3, out _);
        var element = WallElement(20, [3 / 0.3048, 3 / 0.3048, 0], [6 / 0.3048, 3 / 0.3048, 0]);
        element.OwnWallTrimmedAxis = [[3 / 0.3048, 3 / 0.3048, 0], [6.1 / 0.3048, 3 / 0.3048, 0]];

        Assert.Equal(1, new IfcExporter().ApplyCurrentWallExtrusions(f.W, new Dictionary<int, Element> { [20] = element },
            new Dictionary<int, int> { [wall] = 20 }, f.AxisContext));

        var shape = f.Attributes(int.Parse(f.Attributes(wall)[6][1..]));
        var axis = f.Attributes(int.Parse(shape[2].Trim('(', ')').Split(',')[0][1..]));
        var curve = IfcExporter.SplitStepArguments(f.Find(int.Parse(axis[3].Trim('(', ')')[1..])).Args);
        Near([3.1], [f.Numbers(curve[2].Trim('(', ')'))[0]]);
    }

    [Fact]
    public void Brep_PointUsedElsewhere_OrLineOutsideTheBody_IsLeftAsItWas()
    {
        var f = new Fixture();
        int wall = f.BrepWall([(3, 2.9), (6, 2.9), (6, 3.1), (3, 3.1)], 0, 3, out var pointIds);
        f.W.Add("IFCPOLYLINE", $"(#{pointIds[0]},#{pointIds[1]})");     // a point of the body is also used by something else
        var elements = new Dictionary<int, Element> { [20] = WallElement(20, [3 / 0.3048, 3 / 0.3048, 0], [6 / 0.3048, 3 / 0.3048, 0]) };
        Assert.Equal(0, new IfcExporter().ApplyCurrentWallExtrusions(f.W, elements, new Dictionary<int, int> { [wall] = 20 }, f.AxisContext));

        var g = new Fixture();
        int far = g.BrepWall([(3, 2.9), (6, 2.9), (6, 3.1), (3, 3.1)], 0, 3, out _);
        var away = new Dictionary<int, Element> { [20] = WallElement(20, [3 / 0.3048, 30 / 0.3048, 0], [6 / 0.3048, 30 / 0.3048, 0]) };
        Assert.Equal(0, new IfcExporter().ApplyCurrentWallExtrusions(g.W, away, new Dictionary<int, int> { [far] = 20 }, g.AxisContext));
    }

    [Fact]
    public void Brep_WithoutALocationLineOrAnAxisContext_IsLeftAsItWas()
    {
        var f = new Fixture();
        int wall = f.BrepWall([(3, 2.9), (6, 2.9), (6, 3.1), (3, 3.1)], 0, 3, out _);
        var arc = new Element { Id = 20, Location = new LocationCurve { CurveType = "Line", StartPoint = [10, 10, 0], EndPoint = [20, 10, 0], ArcRadius = 5 } };
        var map = new Dictionary<int, int> { [wall] = 20 };

        Assert.Equal(0, new IfcExporter().ApplyCurrentWallExtrusions(f.W, new Dictionary<int, Element> { [20] = arc }, map, f.AxisContext));
        Assert.Equal(0, new IfcExporter().ApplyCurrentWallExtrusions(f.W, new Dictionary<int, Element> { [20] = new Element { Id = 20 } }, map, f.AxisContext));
        Assert.Equal(0, new IfcExporter().ApplyCurrentWallExtrusions(f.W,
            new Dictionary<int, Element> { [20] = WallElement(20, [10, 10, 0], [20, 10, 0]) }, map, 0));
    }
}
