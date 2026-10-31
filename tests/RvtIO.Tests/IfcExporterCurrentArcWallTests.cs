using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The standard-case wall of an arc location curve as the current profile writes it (wall_arc, 90 degrees, radius 3 m, 200 mm thick):
/// the frame at the start of the arc with X along the tangent, the Axis as an IfcTrimmedCurve over an IfcCircle, and the Body as the
/// extrusion of the lower end face with the profile sampler's points over the stored spans.</summary>
public class IfcExporterCurrentArcWallTests
{
    private const double Feet = 1 / 0.3048;
    private const double StoredQuarter = 1.5707963267948974; // a little over eight steps of pi / 16: nine

    private static List<double[]> Arc(double radius, double z, double from, double to, int samples)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= samples; i++)
        {
            double t = from + (to - from) * i / samples;
            points.Add([radius * Math.Cos(t), radius * Math.Sin(t), z]);
        }
        return points;
    }

    /// <summary>The body in feet: an arc wall of 0..90 degrees about the origin, the bottom ring clockwise from above.</summary>
    private static (GeometryData Geometry, Element Element) ArcWall()
    {
        double inner = 2.9 * Feet, outer = 3.1 * Feet, height = 2.7 * Feet;
        Arc ArcAt(double radius, double z) => new([0, 0, z], [1, 0, 0], [0, 1, 0], radius, 0, StoredQuarter);
        var bottomRing = Arc(inner, 0, 0, Math.PI / 2, 3).Concat(Arc(outer, 0, Math.PI / 2, 0, 3)).ToList();
        var topRing = bottomRing.Select(p => new[] { p[0], p[1], height }).AsEnumerable().Reverse().ToList();
        var faces = new List<GeoFace>
        {
            new() { Origin = bottomRing[0], Normal = [0, 0, -1], SourceFace = 1, OuterVertices = bottomRing,
                MeshArcs = [ArcAt(inner, 0), ArcAt(outer, 0)] },
            new() { Origin = topRing[0], Normal = [0, 0, 1], SourceFace = 2, OuterVertices = topRing,
                MeshArcs = [ArcAt(inner, height), ArcAt(outer, height)] },
            // the two end faces
            new() { Origin = [inner, 0, 0], Normal = [0, -1, 0], SourceFace = 3,
                OuterVertices = [[inner, 0, 0], [outer, 0, 0], [outer, 0, height], [inner, 0, height]] },
            new() { Origin = [0, inner, 0], Normal = [1, 0, 0], SourceFace = 4,
                OuterVertices = [[0, outer, 0], [0, inner, 0], [0, inner, height], [0, outer, height]] },
        };
        // the cylinders, each one patch from end to end
        foreach (var (radius, source) in new[] { (inner, 5u), (outer, 6u) })
            faces.Add(new GeoFace
            {
                Origin = [radius, 0, 0], Normal = [1, 0, 0], CurvedSurface = true, SourceFace = source,
                OuterVertices = [[radius, 0, 0], [0, radius, 0], [0, radius, height], [radius, 0, height]],
                AnalyticCylinderOrigin = [0, 0, 0], AnalyticCylinderAxis = [0, 0, 1], AnalyticCylinderReferenceDirection = [1, 0, 0],
                AnalyticCylinderRadius = radius,
            });
        var geometry = new GeometryData { Faces = faces };
        var element = new Element
        {
            Id = 40, Geometry = geometry,
            Location = new LocationCurve
            {
                CurveType = "Arc", StartPoint = [3 * Feet, 0, 0], EndPoint = [0, 3 * Feet, 0], ArcCenter = [0, 0, 0], ArcRadius = 3 * Feet,
                ArcXDirection = [1, 0, 0], ArcYDirection = [0, 1, 0], ArcStartParameter = 0, ArcEndParameter = StoredQuarter,
            },
        };
        return (geometry, element);
    }

    private static (IfcExporterCurrentWallExtrusionTests.Fixture Fixture, int Wall, int Converted) Convert(Element element)
    {
        var f = new IfcExporterCurrentWallExtrusionTests.Fixture();
        int wall = f.StandardCaseWall(1, 1, 0, 1, 0, 3, 0.2, 2.7);
        int converted = new IfcExporter().ApplyCurrentWallExtrusions(f.W, new Dictionary<int, Element> { [40] = element },
            new Dictionary<int, int> { [wall] = 40 }, f.AxisContext);
        return (f, wall, converted);
    }

    [Fact]
    public void ArcWall_IsWrittenOnACircleAxisAndTheSampledProfileOfItsLowerEndFace()
    {
        var (_, element) = ArcWall();
        var (f, wall, converted) = Convert(element);
        Assert.Equal(1, converted);

        // the frame: at the start of the arc, X along the tangent there (+Y)
        var placement = f.Attributes(int.Parse(f.Attributes(wall)[5][1..]));
        var frame = f.Attributes(int.Parse(placement[1][1..]));
        Assert.Equal(3, f.Numbers(frame[0])[0], 9);
        Assert.Equal(0, f.Numbers(frame[0])[1], 9);
        Assert.Equal(new double[] { 0, 1, 0 }, f.Numbers(frame[2]));

        var shape = f.Attributes(int.Parse(f.Attributes(wall)[6][1..]));
        var representations = shape[2].Trim('(', ')').Split(',');
        // the axis: a trimmed circle of radius 3 m about (0, 3 m) in the frame, its reference direction toward the start
        var axisRepresentation = f.Attributes(int.Parse(representations[0][1..]));
        var trimmed = f.Attributes(int.Parse(axisRepresentation[3].Trim('(', ')')[1..]));
        var circle = f.Find(int.Parse(trimmed[0][1..]));
        Assert.Equal("IFCCIRCLE", circle.Type);
        var circleAttributes = IfcExporter.SplitStepArguments(circle.Args);
        Assert.Equal(3, double.Parse(circleAttributes[1], System.Globalization.CultureInfo.InvariantCulture), 9);
        var circleFrame = f.Attributes(int.Parse(circleAttributes[0][1..]));
        Assert.Equal(new double[] { 0, 3, 0 }, f.Numbers(circleFrame[0]).Select(v => Math.Round(v, 9)).ToArray());
        Assert.Equal(new double[] { 0, 0, 1 }, f.Numbers(circleFrame[1]));
        Assert.Equal(new double[] { 0, -1, 0 }, f.Numbers(circleFrame[2]));
        Assert.Equal(new double[] { 0, 0, 0 }, f.Numbers(trimmed[1].Trim('(', ')')));
        Assert.Equal(new double[] { 3, 3, 0 }, f.Numbers(trimmed[2].Trim('(', ')')).Select(v => Math.Round(v, 9)).ToArray());
        Assert.Equal(".T.", trimmed[3]);

        // the body: the plane faces down (the ring is clockwise from above), the extrusion goes up, 9 steps per arc
        var body = f.Attributes(int.Parse(representations[1][1..]));
        var solid = f.Attributes(int.Parse(body[3].Trim('(', ')')[1..]));
        Assert.Equal("2.7", solid[3]);
        var position = f.Attributes(int.Parse(solid[1][1..]));
        Assert.Equal(new double[] { 0, 0, -1 }, f.Numbers(position[1]));
        Assert.Equal(new double[] { 0, 1, 0 }, f.Numbers(position[2]));
        Assert.Equal(new double[] { 0, 0, -1 }, f.Numbers(solid[2]));
        var profile = f.Find(int.Parse(solid[0][1..]));
        Assert.Equal("IFCARBITRARYCLOSEDPROFILEDEF", profile.Type);
        var polyline = f.Find(int.Parse(IfcExporter.SplitStepArguments(profile.Args)[2][1..]));
        var points = polyline.Args.Trim('(', ')').Split(',').Select(token => f.Numbers(token)).ToList();
        Assert.Equal(21, points.Count); // 20 and the closing point
        Assert.Equal(points[0], points[^1]);
        // the origin of the plane is the mean of the four corners, (1.5, 1.5) m in the frame
        Assert.Equal(1.5, f.Numbers(position[0])[0], 9);
        Assert.Equal(1.5, f.Numbers(position[0])[1], 9);
    }

    [Fact]
    public void ArcWall_WhoseBodyIsNotAnExtrusionOfItsEndFace_IsLeftToTheStraightForm()
    {
        var (geometry, element) = ArcWall();
        geometry.Faces[0].MeshArcs = null; // the circular edges of the end face are not known
        var (f, wall, converted) = Convert(element);
        Assert.Equal(1, converted); // the straight form of the writer, with a line for its axis
        var shape = f.Attributes(int.Parse(f.Attributes(wall)[6][1..]));
        var axisRepresentation = f.Attributes(int.Parse(shape[2].Trim('(', ')').Split(',')[0][1..]));
        var trimmed = f.Attributes(int.Parse(axisRepresentation[3].Trim('(', ')')[1..]));
        Assert.Equal("IFCLINE", f.Find(int.Parse(trimmed[0][1..])).Type);
    }
}
