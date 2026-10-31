using RvtIO.Parsing;
using Xunit;
using static RvtIO.Parsing.TfBlockDeserializer;

namespace RvtIO.Tests;

public class WallLocalGeometryTests
{
    [Fact]
    public void PostCutGroupsUseParentIdentityAndIgnoreUnusedInactiveHistoricalFaces()
    {
        var graph = PostCutGraph();
        var groups = SelectOwnPostCutFaceGroups(graph, [6, 8]);
        Assert.NotNull(groups);
        Assert.Equal(6u, groups[6]);
        Assert.Equal(6u, groups[8]);
    }

    [Theory]
    [InlineData("inactive")] [InlineData("invisible")] [InlineData("unknown-face")]
    [InlineData("empty")] [InlineData("hidden-extra-solid")]
    public void PostCutGroupsRejectUnconfirmedVisibilityOwnershipOrFilteredSolids(string mode)
    {
        var graph = PostCutGraph();
        uint[] used = mode == "empty" ? [] : mode == "unknown-face" ? [99] : [6, 8];
        if (mode == "inactive") graph = ReplaceInfo(graph, 5, 19, 0);
        if (mode == "invisible") graph = ReplaceInfo(graph, 5, 19, 0x800e0);
        if (mode == "hidden-extra-solid")
        {
            var nodes = graph.Nodes.ToArray();
            nodes[0] = nodes[0] with { Fields = [Info(93, 0), Array(2009,"m_subNodes",Ptr(0,2015),Ptr(1,2092))] };
            graph = ReplaceInfo(graph with { Nodes = nodes }, 0, 17, 0);
        }
        Assert.Null(SelectOwnPostCutFaceGroups(graph, used));
    }

    private static OwnGElementGeometryGraph PostCutGraph()
    {
        var graph = Graph(); var nodes = graph.Nodes.ToArray();
        nodes[0] = nodes[0] with { Fields = [Info(93,0),Array(2009,"m_subNodes",Ptr(1,2092))] };
        return ReplaceInfo(graph with { Nodes = nodes }, 5, 19, 0x80000);
    }

    [Fact]
    public void LocalVisitorUsesDepthFirstSceneOrderAndDirectGeometryIdentityNotHandleOrFlags()
    {
        var graph=Graph();var value=Read(graph,out var failure)!;
        Assert.Null(failure);Assert.Equal(93,value.OwnerId);Assert.Equal(1,value.FirstDirectGeometryQueueIndex);
        Assert.Equal(new[]{-1,0},value.VisitedGroupQueueIndices);
        Assert.Equal(new[]{3,4,1},value.Geometries.Select(g=>g.QueueIndex));
        Assert.Equal(new[]{2},value.SkippedNodeQueueIndices);
        Assert.All(graph.Nodes.Skip(1),n=>Assert.Equal(7u,n.Handle));
    }

    [Fact]
    public void FaceIteratorIncludesParentThenOneLevelOfRegionsWithoutFilteringFlag19()
    {
        var value=Read(Graph(),out _)!;var geometry=value.Geometries.Single(g=>g.QueueIndex==1);
        Assert.Equal(new[]{5,7},geometry.Faces.Select(f=>f.FaceQueueIndex));
        Assert.Equal(new int?[]{null,5},geometry.Faces.Select(f=>f.RegionOfQueueIndex));
        Assert.Equal(new[]{19,21},geometry.Faces.Select(f=>f.Tag));
        Assert.Equal(new uint[]{0,uint.MaxValue},geometry.Faces.Select(f=>f.Flags));
        Assert.DoesNotContain(geometry.Faces,f=>f.FaceQueueIndex==9); // region of region is not recursively visited
        Assert.Equal(new int?[]{6,null},geometry.EdgeQueueIndices);
        Assert.Null(geometry.Faces[0].SurfacePointer.QueueIndex); // external remains explicitly external
        Assert.Equal(77u,geometry.Faces[0].SurfacePointer.Handle);
        Assert.Equal((ushort)0,geometry.Faces[0].SurfacePointer.ClassId);
    }

    [Theory]
    [InlineData(-2)] [InlineData(0)] [InlineData(93)] [InlineData(int.MinValue)]
    public void TaggedGroupDoesNotExposeItsChildren(int tag)
    {
        var graph=Graph();graph=ReplaceInfo(graph,0,tag,uint.MaxValue);
        var value=Read(graph,out var failure)!;
        Assert.Null(failure);Assert.Equal(new[]{1},value.Geometries.Select(g=>g.QueueIndex));
        Assert.Equal(new[]{0,2},value.SkippedNodeQueueIndices);Assert.Equal(new[]{-1},value.VisitedGroupQueueIndices);
    }

    [Theory]
    [InlineData(-1)] [InlineData(-2)] [InlineData(0)] [InlineData(93)]
    public void BRepIsIncludedBeforeCheckingItsTag(int tag)
    {
        var graph=ReplaceInfo(Graph(),1,tag,0);
        Assert.Contains(Read(graph,out var failure)!.Geometries,g=>g.QueueIndex==1);Assert.Null(failure);
    }

    [Theory]
    [InlineData(1991)] [InlineData(1736)] [InlineData(1974)]
    public void UntaggedComponentOrCurveHasSourceBoundZeroSceneChildren(int cls)
    {
        var graph=ReplaceLeaf(Graph(),(ushort)cls,-1);
        var value=Read(graph,out var failure)!;
        Assert.Null(failure);Assert.Equal(3,value.Geometries.Count);Assert.Empty(value.SkippedNodeQueueIndices);
    }

    [Fact]
    public void UntaggedKindWithUnconfirmedVirtualChildCountRejectsAtomically()
    {
        var graph=ReplaceLeaf(Graph(),2014,-1);
        Assert.Null(Read(graph,out var failure));Assert.Equal("untagged-child-count:2:2014",failure);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void NullSceneSlotIsSkippedButExternalAndHandSlotsAreNotAssumedNonBRep(int kind)
    {
        var graph=Graph();var nodes=graph.Nodes.ToArray();var pointer=kind switch
        {
            0=>new GElementGraphPointer(0,null,null,null,[],0,0),
            1=>new GElementGraphPointer(77,0,null,null,[],0,0),
            _=>new GElementGraphPointer(77,4,null,8,[],0,0)
        };
        nodes[0]=nodes[0] with{Fields=[Info(93,0),Array(2009,"m_subNodes",pointer)]};
        var value=Read(graph with{Nodes=nodes},out var failure);
        if(kind==0){Assert.NotNull(value);Assert.Null(failure);Assert.Empty(value.Geometries);Assert.Null(value.FirstDirectGeometryQueueIndex);}
        else{Assert.Null(value);Assert.Equal("children:-1",failure);}
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void InvalidFacePointerIsNotSkippedAsAbsentGeometry(int kind)
    {
        var graph=Graph();var nodes=graph.Nodes.ToArray();var geometry=nodes[2];
        var pointer=kind switch
        {
            0=>new GElementGraphPointer(0,null,null,null,[],0,0),
            1=>new GElementGraphPointer(77,0,null,null,[],0,0),
            _=>new GElementGraphPointer(7,1243,6,null,[],0,0)
        };
        nodes[2]=geometry with{Fields=[Info(19,0),Array(1978,"m_pFaces",pointer),Array(2092,"m_pEdges")]};
        Assert.Null(Read(graph with{Nodes=nodes},out var failure));Assert.NotNull(failure);
    }

    [Fact]
    public void PositiveTaggedLeafDoesNotNeedAnUnconfirmedChildCountImplementation()
    {
        var graph=ReplaceLeaf(Graph(),2014,7);
        var value=Read(graph,out var failure)!;Assert.Null(failure);Assert.Contains(2,value.SkippedNodeQueueIndices);
    }

    [Fact]
    public void InvalidQueueIndicesAndRepeatedSceneIdentityRejectWithoutOverflowOrCycle()
    {
        var graph=Graph();var nodes=graph.Nodes.ToArray();nodes[1]=nodes[1] with{QueueIndex=int.MaxValue};
        Assert.Null(Read(graph with{Nodes=nodes},out var failure));Assert.Equal("graph",failure);
        nodes=graph.Nodes.ToArray();nodes[1]=nodes[1] with{Fields=[Info(-1,0),Array(2009,"m_subNodes",Ptr(0,2015))]};
        Assert.Null(Read(graph with{Nodes=nodes},out failure));Assert.Equal("visit:0",failure);
    }

    private static OwnWallLocalGeometryInputs? Read(OwnGElementGeometryGraph graph,out string? failure)
        =>SelectOwnWallLocalGeometryInputs(graph,out failure);
    private static GElementGraphField Info(int tag,uint flags)=>new(1246,"m_GInfo",null,0,0,
        [new(0,0,[tag,int.MinValue,int.MaxValue,flags],null,[])]);
    private static GElementGraphPointer Ptr(int index,ushort cls)=>new(7,cls,index,null,[],0,0);
    private static GElementGraphField Array(int declaring,string name,params GElementGraphPointer[] pointers)
        =>new(declaring,name,(uint)pointers.Length,0,0,pointers.Select(p=>new GElementGraphItem(0,0,[],p,[])).ToArray());
    private static GElementGraphNode Node(int index,ushort cls,int tag,uint flags,params GElementGraphField[] fields)
        =>new(index,cls,index==-1?93u:7u,0,0,[Info(tag,flags),..fields]);
    private static OwnGElementGeometryGraph ReplaceInfo(OwnGElementGeometryGraph graph,int index,int tag,uint flags)
    {
        var nodes=graph.Nodes.ToArray();var node=nodes[index+1];nodes[index+1]=node with{Fields=[Info(tag,flags),..node.Fields.Skip(1)]};
        return graph with{Nodes=nodes};
    }
    private static OwnGElementGeometryGraph ReplaceLeaf(OwnGElementGeometryGraph graph,ushort cls,int tag)
    {
        var nodes=graph.Nodes.ToArray();nodes[3]=Node(2,cls,tag,uint.MaxValue);
        nodes[0]=nodes[0] with{Fields=[Info(93,0),Array(2009,"m_subNodes",Ptr(0,2015),Ptr(1,2092),Ptr(2,cls))]};
        return graph with{Nodes=nodes};
    }
    internal static OwnGElementGeometryGraph Graph()=>new(93,0,
    [
        Node(-1,2007,93,0,Array(2009,"m_subNodes",Ptr(0,2015),Ptr(1,2092),Ptr(2,1736))),
        Node(0,2015,-1,uint.MaxValue,Array(2009,"m_subNodes",Ptr(3,2092),Ptr(4,2092))),
        Node(1,2092,-2,0,Array(1978,"m_pFaces",Ptr(5,1589)),Array(2092,"m_pEdges",Ptr(6,1243),new(0,null,null,null,[],0,0))),
        Node(2,1736,7,uint.MaxValue),
        Node(3,2092,19,uint.MaxValue,Array(1978,"m_pFaces"),Array(2092,"m_pEdges")),
        Node(4,2092,21,0,Array(1978,"m_pFaces"),Array(2092,"m_pEdges")),
        Node(5,1589,19,0,Array(1590,"m_faceRegions",Ptr(7,1589)),
            new(1589,"m_pSurf",null,0,0,[new(0,0,[],new(77,0,null,null,[],0,0),[])])),
        Node(6,1243,20,0),
        Node(7,1589,21,uint.MaxValue,Array(1590,"m_faceRegions",Ptr(9,1589)),
            new(1589,"m_pSurf",null,0,0,[new(0,0,[],new(0,null,null,null,[],0,0),[])])),
        Node(8,2014,21,0),Node(9,1589,23,0)
    ],[],0);
}
