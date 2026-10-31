using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The opening of a door or window in a straight wall from the cut loop of its family symbol (the current profile);
/// the numbers are those of Test_2022 door 422466 as the expected export writes it</summary>
public class IfcExporterCurrentDoorWindowOpeningTests
{
    private const int WallElement = 30, DoorElement = 31, SymbolElement = 50;

    private static (IfcExporterCurrentWallExtrusionTests.Fixture Fixture, int Opening, Dictionary<int, Element> Elements, Dictionary<int, int> Products)
        Scene(double[] symbolBox)
    {
        var f = new IfcExporterCurrentWallExtrusionTests.Fixture();
        // the wall runs along -X; its frame stands at the start of the axis, 9.9009 m / 1.2384 m (the site placement above is no part of the model frame)
        int wall = f.StandardCaseWall(9.900931517437637, 1.2384060214181773, 0, -1, 0, 3, 0.202, 2.7);

        int openingPoint = f.Point3D(0, 0, 0);
        int openingFrame = f.W.Add("IFCAXIS2PLACEMENT3D", $"#{openingPoint},$,$");
        int openingPlacement = f.W.Add("IFCLOCALPLACEMENT", $"#{f.StoreyPlacement},#{openingFrame}");
        int body = f.W.Add("IFCSHAPEREPRESENTATION", $"#{f.BodyContext},'Body','SweptSolid',($)");
        int shape = f.W.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,(#{body})");
        int opening = f.W.Add("IFCOPENINGELEMENT", $"'g',#1,'Single-Flush:800 x 2100:31',$,'Opening',#{openingPlacement},#{shape},'31'");
        int door = f.W.Add("IFCDOOR", $"'d',#1,'Single-Flush',$,$,#{openingPlacement},$,'31'");
        f.W.Add("IFCRELVOIDSELEMENT", $"'v',#1,$,$,#{wall},#{opening}");
        f.W.Add("IFCRELFILLSELEMENT", $"'f',#1,$,$,#{opening},#{door}");

        double[][] loop = [[-1.3123359580052494, 0.3313648293963368, 0], [1.3123359580052485, 0.33136482939633194, 0],
            [1.3123359580052476, 0.33136482939633194, 6.889763779527558], [-1.3123359580052498, 0.3313648293963368, 6.889763779527558]];
        double[] transform = [1, 3.3508640216608995E-15, 0, -3.3508640216608995E-15, 1, 0, 0, 0, 1, 30.97746560839118, 4.063011881293229, 0];
        var elements = new Dictionary<int, Element>
        {
            [WallElement] = new() { Id = WallElement, Location = new LocationCurve { CurveType = "Line", StartPoint = [32.8147, 4.063, 0], EndPoint = [22.8377, 4.063, 0] } },
            [DoorElement] = new()
            {
                Id = DoorElement, TypeId = 1,
                OwnSingleFamilyCutout = (SymbolElement, WallElement, transform, new FamilyCutLoopData(loop, [1.864073392952492E-15, -1, 0], 0)),
            },
            [SymbolElement] = new() { Id = SymbolElement, Geometry = new GeometryData { BoundingBox = [0, 0, 0, 0, 0, 0], RootBoundingBox = symbolBox } },
        };
        return (f, opening, elements, new Dictionary<int, int> { [wall] = WallElement, [door] = DoorElement });
    }

    private static (string[] Placement, string[] Solid, string[] Profile) Read(IfcExporterCurrentWallExtrusionTests.Fixture f, int opening)
    {
        var attributes = f.Attributes(opening);
        var placement = f.Attributes(int.Parse(attributes[5][1..]));
        var shape = f.Attributes(int.Parse(attributes[6][1..]));
        var representation = f.Attributes(int.Parse(shape[2].Trim('(', ')')[1..]));
        var solid = f.Attributes(int.Parse(representation[3].Trim('(', ')')[1..]));
        return (placement, solid, f.Attributes(int.Parse(solid[0][1..])));
    }

    [Fact]
    public void Door_IsPlacedInTheFrameOfTheWallAndExtrudedAcrossItsWholeWidth()
    {
        // the box of the symbol reaches 2.96 ft to the side the cut plane is on: the hinge side
        var (f, opening, elements, products) = Scene([-1.5748031496062993, -2.9564656610201734, 0, 1.5748031496062993, 0.4297900262467311, 7.152230971128607]);

        Assert.Equal(1, new IfcExporter().ApplyCurrentDoorWindowOpenings(f.W, elements, products));

        var (placement, solid, profile) = Read(f, opening);
        // relative to the placement of the wall, at the lowest corner of the loop
        var origin = f.Numbers(f.Attributes(int.Parse(placement[1][1..]))[0]);
        Assert.Equal(0.059, origin[0], 4);
        Assert.Equal(-0.101, origin[1], 6);
        Assert.Equal(0, origin[2], 9);
        // the plane of the loop faces the wall (Z = the direction of the cut), X vertical; the extrusion across the wall
        var position = f.Attributes(int.Parse(solid[1][1..]));
        Assert.Equal(new double[] { 0, 0, 0 }, f.Numbers(position[0]));
        Assert.Equal(new double[] { 0, 1, 0 }, f.Numbers(position[1]).Select(v => Math.Round(v, 9)).ToArray());
        Assert.Equal(new double[] { 0, 0, 1 }, f.Numbers(position[2]));
        Assert.Equal(new double[] { 0, 0, 1 }, f.Numbers(solid[2]));
        Assert.Equal(0.202, double.Parse(solid[3], System.Globalization.CultureInfo.InvariantCulture), 9);
        // the loop of the symbol starts at the bottom: XDim is the height, YDim the width
        Assert.Equal("IFCRECTANGLEPROFILEDEF", f.Find(int.Parse(solid[0][1..])).Type);
        Assert.Equal(".AREA.,''", $"{profile[0]},{profile[1]}");
        Assert.Equal(2.1, double.Parse(profile[3], System.Globalization.CultureInfo.InvariantCulture), 4);
        Assert.Equal(0.8, double.Parse(profile[4], System.Globalization.CultureInfo.InvariantCulture), 4);
        var profilePlacement = f.Attributes(int.Parse(profile[2][1..]));
        Assert.Equal(1.05, f.Numbers(profilePlacement[0])[0], 4);
        Assert.Equal(0.4, f.Numbers(profilePlacement[0])[1], 4);
        Assert.Equal(new double[] { -1, 0 }, f.Numbers(profilePlacement[1]).Select(v => Math.Round(v, 9)).ToArray());
    }

    [Fact]
    public void Door_OnTheOtherSideOfTheAxis_IsExtrudedFromTheOtherFaceOfTheWall()
    {
        // the box of the symbol mirrored: the hinge is on the other side, the plane on the other face, the direction turned
        var (f, opening, elements, products) = Scene([-1.5748031496062993, -0.4297900262467311, 0, 1.5748031496062993, 2.9564656610201734, 7.152230971128607]);

        Assert.Equal(1, new IfcExporter().ApplyCurrentDoorWindowOpenings(f.W, elements, products));

        var (placement, solid, _) = Read(f, opening);
        Assert.Equal(0.101, f.Numbers(f.Attributes(int.Parse(placement[1][1..]))[0])[1], 6);
        Assert.Equal(new double[] { 0, -1, 0 }, f.Numbers(f.Attributes(int.Parse(solid[1][1..]))[1]).Select(v => Math.Round(v, 9)).ToArray());
    }

    [Fact]
    public void OpeningWithoutACutLoopOrASymbolBox_IsLeftAsItWas()
    {
        var (f, opening, elements, products) = Scene([-1, -1, 0, 1, 1, 1]);
        elements[DoorElement].OwnSingleFamilyCutout = null;
        string before = f.Find(opening).Args;
        Assert.Equal(0, new IfcExporter().ApplyCurrentDoorWindowOpenings(f.W, elements, products));
        Assert.Equal(before, f.Find(opening).Args);

        var (g, other, otherElements, otherProducts) = Scene([-1, -1, 0, 1, 1, 1]);
        otherElements[SymbolElement].Geometry = null;
        Assert.Equal(0, new IfcExporter().ApplyCurrentDoorWindowOpenings(g.W, otherElements, otherProducts));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ArbitraryLineLoopKeepsItsFirstVertexAndTurnsCounterClockwise(bool reversed, bool quadrilateral)
    {
        var (f, opening, elements, products) = Scene([-1, -1, 0, 1, 1, 3]);
        var own = elements[DoorElement].OwnSingleFamilyCutout!.Value;
        double y = own.Loop.Vertices[0][1];
        List<double[]> vertices = quadrilateral
            ? [[-1, y, 0], [1, y, 0], [0.5, y, 2], [-1, y, 2]]
            : [[-1, y, 0], [1, y, 0], [1, y, 2], [0, y, 3], [-1, y, 2]];
        if (reversed) vertices = [vertices[0], .. vertices.Skip(1).Reverse()];
        elements[DoorElement].OwnSingleFamilyCutout = (own.SymbolId, own.HostId, own.Transform, own.Loop with { Vertices = vertices });

        Assert.Equal(1, new IfcExporter().ApplyCurrentDoorWindowOpenings(f.W, elements, products));

        var (_, solid, profile) = Read(f, opening);
        Assert.Equal("IFCARBITRARYCLOSEDPROFILEDEF", f.Find(int.Parse(solid[0][1..])).Type);
        Assert.Equal(".AREA.,''", $"{profile[0]},{profile[1]}");
        var curve = f.Attributes(int.Parse(profile[2][1..]));
        var points = curve[0].Trim('(', ')').Split(',').Select(f.Numbers).ToList();
        Assert.Equal(vertices.Count + 1, points.Count);
        Assert.Equal(points[0], points[^1]);
        Assert.Equal(0, points[0][0], 12);
        Assert.Equal(0.6096, points[0][1], 12);
        double area = Enumerable.Range(0, points.Count - 1)
            .Sum(i => points[i][0] * points[i + 1][1] - points[i + 1][0] * points[i][1]) / 2;
        Assert.True(area > 0);
        Assert.Equal((quadrilateral ? 3.5 : 5) * 0.3048 * 0.3048, area, 12);
    }

    [Theory]
    [InlineData("few-vertices")]
    [InlineData("nonfinite")]
    [InlineData("collinear")]
    public void InvalidLineLoopsLeaveTheOpeningAndWriterUnchanged(string mode)
    {
        var (f, opening, elements, products) = Scene([-1, -1, 0, 1, 1, 3]);
        var own = elements[DoorElement].OwnSingleFamilyCutout!.Value;
        List<double[]> vertices = mode switch
        {
            "few-vertices" => [[0, 0, 0], [1, 0, 0]],
            "nonfinite" => [[0, 0, 0], [1, 0, 0], [double.NaN, 0, 1]],
            _ => [[0, 0, 0], [1, 0, 0], [2, 0, 0]],
        };
        elements[DoorElement].OwnSingleFamilyCutout = (own.SymbolId, own.HostId, own.Transform, own.Loop with { Vertices = vertices });
        string before = f.Find(opening).Args;
        int count = f.W.EntityCount;

        Assert.Equal(0, new IfcExporter().ApplyCurrentDoorWindowOpenings(f.W, elements, products));

        Assert.Equal(before, f.Find(opening).Args);
        Assert.Equal(count, f.W.EntityCount);
    }
}
