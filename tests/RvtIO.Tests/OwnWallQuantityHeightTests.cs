using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnWallQuantityHeightTests
{
    [Fact]
    public void SimpleWallHeightComesFromCollectedSourceSolidsBeforeGrossReconstruction()
    {
        var wall = Wall();
        wall.BoundingBox = [0,0,0,1,1,99];
        wall.Parameters.Add(new() { Id = -1001105, Value = 99.0 });
        Assert.Equal(1.6 * 0.3048, IfcExporter.OwnSimpleWallHeightCurrent(wall)!.Value, 12);
    }

    [Fact]
    public void InactiveTiltKeepsTheBodyExporterHeightInsteadOfTheAxisRouteHeight()
    {
        var wall = Wall();
        wall.Geometry = wall.OwnCurrentCutPairBodyCurrent;
        foreach (var face in wall.Geometry!.Faces) face.GeometryNode = 7;
        wall.Geometry.InactiveFaces = [new() { GeometryNode = 7, Normal = [1, 1, 1] }];

        Assert.Null(IfcExporter.OwnSimpleWallHeightCurrent(wall));
    }

    [Theory]
    [InlineData("no-source")] [InlineData("open-source")] [InlineData("two-solids")]
    [InlineData("incomplete-coverage")] [InlineData("no-helper")] [InlineData("nonrectangular-helper")]
    [InlineData("slanted")] [InlineData("nonfinite")]
    public void UnsupportedOrUnconfirmedSourceKeepsTheWrittenExtrusionHeight(string mutation)
    {
        var wall = Wall();
        switch (mutation)
        {
            case "no-source": wall.OwnCurrentCutPairBodyCurrent = null; break;
            case "open-source": wall.OwnCurrentCutPairBodyCurrent!.BrepSolids![0].IsClosed = false; break;
            case "two-solids": wall.OwnCurrentCutPairBodyCurrent!.BrepSolids!.Add(new() { IsClosed = true }); break;
            case "incomplete-coverage": wall.OwnCurrentCutPairBodyCurrent!.BrepSolids![0].FaceIndices.RemoveAt(0); break;
            case "no-helper": wall.OwnWallHelper = null; break;
            case "nonrectangular-helper": wall.OwnWallHelper = new(3524, true, true, [8]); break;
            case "slanted": wall.OwnWallCrossSection = 0; break;
            case "nonfinite": wall.OwnCurrentCutPairBodyCurrent!.Faces[0].OuterVertices![0][2] = double.NaN; break;
        }
        Assert.Null(IfcExporter.OwnSimpleWallHeightCurrent(wall));
    }

    [Fact]
    public void StandardCaseWallExtrusionDepthIsUpdatedFromSourceSolids()
    {
        var wall = Wall();
        wall.Id = 42;
        var f = new IfcExporterCurrentWallExtrusionTests.Fixture();
        int wallEntity = f.StandardCaseWall(1, 2, 0.3, 0, 1, 3.2, 0.2, 3.7);
        var elements = new Dictionary<int, Element> { [42] = wall };
        var productToElement = new Dictionary<int, int> { [wallEntity] = 42 };

        Assert.Equal(1, new IfcExporter().ApplyCurrentWallExtrusions(f.W, elements, productToElement));

        var attributes = f.Attributes(wallEntity);
        var representations = f.Attributes(int.Parse(attributes[6][1..]));
        var ids = representations[2].Trim('(', ')').Split(',').Select(token => int.Parse(token[1..])).ToList();
        var body = f.Attributes(ids[1]);
        var solid = f.Attributes(int.Parse(body[3].Trim('(', ')')[1..]));
        Assert.Equal(StepWriter.Real(1.6 * 0.3048), solid[3]);
    }

    private static Element Wall()
    {
        var source = OwnWallPlanarBodyTests.Cube();
        foreach (var face in source.Faces)
            face.Vertices = face.Vertices!.Select(p => new[] { p[0],p[1],2.1+p[2]*1.6 }).ToList();
        return new() { OwnWallCrossSection = 1, OwnWallHelper = new(3524, true, true, [4]),
            OwnCurrentCutPairBodyCurrent = ElementBuilder.BuildGeometryData(source) };
    }
}
