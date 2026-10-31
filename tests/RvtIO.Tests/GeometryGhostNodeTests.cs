using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Geometry nodes the expected export exports as solids that leave no polygon in the decoded geometry: the second of two
/// identical nodes (the sample project A toilet kit) and a node of display surfaces only (the Silk_White_V1 node of the
/// shower cabin). Both stay known as ghost nodes, for the material list of the product.</summary>
public class GeometryGhostNodeTests
{
    private static uint handle;

    /// <summary>Four faces (the fewest of a solid) at x = start .. start + 3 of one node, each with its own face handle.</summary>
    private static List<GRepFace> Node(double start, int node, int? material, int style = -1, int count = 4) =>
        Enumerable.Range(0, count).Select(index => new GRepFace
        {
            Vertices = [[start + index, 0, 0], [start + index + 1, 0, 0], [start + index, 1, 0]],
            Origin = [start + index, 0, 0],
            XVector = [0, 0, 1],
            GeometryNode = node,
            GeometryStyle = style,
            RenderStyleId = material,
            SourceFace = ++handle,
        }).ToList();

    [Fact]
    public void NodeWhosePolygonsAllDuplicateAnotherNode_IsAGhostNodeWithItsMaterial()
    {
        var grep = new GRepData
        {
            BoundingBox = [0, 0, 0, 20, 1, 1],
            Faces = [.. Node(0, 1, 700), .. Node(0, 2, 700, 9), .. Node(10, 3, 710)],
        };
        var geometry = ElementBuilder.BuildGeometryData(grep)!;

        Assert.Equal(8, geometry.Faces.Count);
        Assert.Equal([(2, 9, 700)], geometry.GhostNodes);
    }

    [Fact]
    public void PartiallyDuplicatedNode_KeepsItsOwnPolygonsAndIsNoGhostNode()
    {
        var second = Node(0, 2, 700);
        second[3] = Node(50, 2, 700, count: 1)[0];
        var grep = new GRepData
        {
            BoundingBox = [0, 0, 0, 60, 1, 1],
            Faces = [.. Node(0, 1, 700), .. second],
        };
        var geometry = ElementBuilder.BuildGeometryData(grep)!;

        Assert.Equal(5, geometry.Faces.Count);
        Assert.Null(geometry.GhostNodes);
    }

    [Fact]
    public void DuplicatePolygonsInsideOneNode_AreNoGhostNode()
    {
        var node = Node(0, 1, 700);
        var grep = new GRepData
        {
            BoundingBox = [0, 0, 0, 20, 1, 1],
            Faces = [.. node, .. node.Select(face => face with { SourceFace = ++handle }), .. Node(10, 2, 710)],
        };
        var geometry = ElementBuilder.BuildGeometryData(grep)!;

        Assert.Equal(8, geometry.Faces.Count);
        Assert.Null(geometry.GhostNodes);
    }

    [Fact]
    public void NodeTooSmallForASolid_AndFacesThatKnowNoNode_AreNeverGhostNodes()
    {
        var grep = new GRepData
        {
            BoundingBox = [0, 0, 0, 20, 1, 1],
            Faces = [.. Node(0, 1, 700), .. Node(0, 2, 700, count: 3), .. Node(0, 0, 700)],
        };
        Assert.Null(ElementBuilder.BuildGeometryData(grep)!.GhostNodes);
    }

    [Fact]
    public void DisplayOnlyNodes_ComeThroughNextToTheDuplicateOnes()
    {
        var grep = new GRepData
        {
            BoundingBox = [0, 0, 0, 5, 1, 1],
            Faces = [.. Node(0, 1, 700), .. Node(0, 2, 700)],
            DisplayOnlyNodes = [(5, -1, 720)],
        };
        var geometry = ElementBuilder.BuildGeometryData(grep)!;

        Assert.Equal([(2, -1, 700), (5, -1, 720)], geometry.GhostNodes);
    }

    [Theory]
    [InlineData((ushort)1999, null, 3, true)]      // detail level (the cabin: != Coarse)
    [InlineData((ushort)2002, null, 3, true)]
    [InlineData((ushort)1999, null, 1, false)]     // another parameter (the Test_2022 door symbols: parameter 1)
    [InlineData((ushort)1996, true, null, true)]   // negated view direction (the cabin: not seen from above)
    [InlineData((ushort)1996, false, null, false)] // an elevation or plan symbol
    [InlineData((ushort)1996, null, null, false)]
    [InlineData((ushort)1995, true, null, false)]  // a cut condition is not evaluated here
    public void DisplaySurfaces_AreTakenOnlyBelowConditionsTheDecoderEvaluates(ushort cls, bool? negated, int? parameter, bool expected) =>
        Assert.Equal(expected, GRepParserV2.IsEvaluatedModelCondition(
            new GRepParserV2.SerializedModelCondition(cls, negated, null, parameter, null)));

    [Theory]
    [InlineData(0x0028C204u, false)]
    [InlineData(0x0008C204u, true)]
    [InlineData(0x0408C204u, false)]   // auxiliary compound-layer surface
    [InlineData(0x4008C204u, false)]   // suppressed
    public void BodySurface_StillExcludesDisplayAuxiliaryAndSuppressedFaces(uint flags, bool expected) =>
        Assert.Equal(expected, GRepParserV2.IsPhysicalBodySurface(flags));
}
