using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class PlanarSpaceTrimCoverageTests
{
    // Owner 6957635: complete 5,167-byte spatial cache with nine authored Faces.
    // Face handle 10 has two disjoint coplanar contours, yielding ten IFC faces.
    private const string SplitFixture = "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";

    [Fact]
    public void SerializedFaceWithDisjointRegions_RetainsEveryRegion()
    {
        byte[] block = Convert.FromBase64String(SplitFixture);
        uint size = BitConverter.ToUInt32(block, 8);
        var geometry = Assert.IsType<GRepData>(
            GRepParserV2.ParseExactPlanarSpaceBoundary(block, 0, size, 6957635));
        Assert.Equal(10, geometry.Faces.Count);
        var solid = Assert.Single(geometry.Solids);
        Assert.True(solid.IsClosed);
        Assert.Equal(Enumerable.Range(0, 10), solid.FaceIndices.Order());
        var model = Assert.IsType<RvtIO.Model.GeometryData>(ElementBuilder.BuildGeometryData(geometry));
        Assert.True(RvtIO.Ifc.IfcExporter.TryOrientExactFacetedShell(model.Faces,
            solid.FaceIndices, out var oriented, triangulateNonPlanarFaces: false));
        Assert.Equal(10, oriented.Count);
        Assert.Null(GRepParserV2.Parse(block, 0, size));
        Assert.Equal(Convert.FromBase64String(SplitFixture), block);
    }

    [Fact]
    public void DisjointContoursAndHole_ExactlyCoverSourceSegmentsInEitherDirection()
    {
        var outer = Square(0, 0, 4);
        var hole = Square(1, 1, 1);
        var island = Square(6, 0, 1);
        var source = Segments(outer).Concat(Segments(hole)).Concat(Segments(island)).AsEnumerable().Reverse().ToList();
        hole.Reverse(); island.Reverse();
        Assert.True(GRepParserV2.HasExactPlanarTrimCoverage(source,
            [new GRepFace { Vertices = outer, InnerVertices = [hole] }, new GRepFace { Vertices = island }]));
    }

    [Theory]
    [InlineData("lost-region")]
    [InlineData("duplicate-region")]
    [InlineData("invented-region")]
    [InlineData("duplicate-source-edge")]
    [InlineData("open-chain")]
    [InlineData("chord")]
    [InlineData("touching-cycles")]
    [InlineData("collapsed-edge")]
    [InlineData("changed-endpoint")]
    [InlineData("nonfinite")]
    [InlineData("overflow")]
    [InlineData("wrong-dimension")]
    [InlineData("missing-outer")]
    [InlineData("short-hole")]
    [InlineData("empty-source")]
    public void PartialDuplicateBranchedOrInvalidContours_AreRejected(string mutation)
    {
        var first = Square(0, 0, 1);
        var second = Square(3, 0, 1);
        var source = Segments(first).Concat(Segments(second)).ToList();
        var output = new List<GRepFace>
        {
            new() { Vertices = first.Select(point => (double[])point.Clone()).ToList() },
            new() { Vertices = second.Select(point => (double[])point.Clone()).ToList() },
        };
        switch (mutation)
        {
            case "lost-region": output.RemoveAt(1); break;
            case "duplicate-region": output.Add(output[1]); break;
            case "invented-region": output.Add(new GRepFace { Vertices = Square(6, 0, 1) }); break;
            case "duplicate-source-edge": source.Add(source[0]); break;
            case "open-chain": source.Add(([0, 0, 0], [-1, 0, 0])); break;
            case "chord": source.Add((first[0], first[2])); break;
            case "touching-cycles":
                var touching = Square(1, 1, 1);
                source.AddRange(Segments(touching)); output.Add(new GRepFace { Vertices = touching }); break;
            case "collapsed-edge": source.Add((first[0], first[0])); break;
            case "changed-endpoint": output[0].Vertices![0][0] += 2e-5 / .3048; break;
            case "nonfinite": output[0].Vertices![0][0] = double.NaN; break;
            case "overflow": source[0] = ([double.MaxValue, 0, 0], first[1]); break;
            case "wrong-dimension": output[0].Vertices![0] = [0, 0]; break;
            case "missing-outer": output[0].Vertices = null; break;
            case "short-hole": output[0].InnerVertices = [[first[0], first[1]]]; break;
            case "empty-source": source.Clear(); break;
        }
        Assert.False(GRepParserV2.HasExactPlanarTrimCoverage(source, output));
    }

    [Fact]
    public void Coverage_UsesExistingWriterLatticeWithoutChangingCoordinates()
    {
        var source = Square(0, 0, 1);
        var output = source.Select(point => (double[])point.Clone()).ToList();
        double smallRoundoff = 1e-8;
        output[0][0] = smallRoundoff;
        Assert.True(GRepParserV2.HasExactPlanarTrimCoverage(Segments(source),
            [new GRepFace { Vertices = output }]));
        Assert.Equal(smallRoundoff, output[0][0]);
        Assert.Equal(0, source[0][0]);
    }

    private static List<double[]> Square(double x, double y, double size) =>
        [[x,y,0], [x+size,y,0], [x+size,y+size,0], [x,y+size,0]];

    private static List<(double[] A, double[] B)> Segments(List<double[]> ring) =>
        ring.Select((point, index) => (point, ring[(index + 1) % ring.Count])).ToList();
}
