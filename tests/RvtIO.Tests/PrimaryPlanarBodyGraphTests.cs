using RvtIO.Parsing;
using static RvtIO.Parsing.TfBlockDeserializer;
using F = RvtIO.Tests.GElementPointWithinFirstSampleTests;
using Xunit;

namespace RvtIO.Tests;

public class PrimaryPlanarBodyGraphTests
{
    [Theory]
    [InlineData(null)] [InlineData("open")] [InlineData("missing-edge")]
    [InlineData("wrong-face-side")] [InlineData("missing-surface")]
    [InlineData("no-direct-body")] [InlineData("two-local-bodies")]
    public void WholeOwnCubeRequiresDirectIdentityBothEdgeSidesAndAllPlanes(string? mode)
    {
        var graph = CubeGraph();
        if (mode == "open") graph = F.Replace(graph, 19, Scalar(1255, "m_open", 1));
        if (mode == "missing-edge") graph = F.Replace(graph, 0, Pointers(2092, "m_pEdges",
            Enumerable.Range(8,11).Select(q => (q,(ushort)1243)).ToArray()));
        if (mode == "wrong-face-side") graph = F.Replace(graph, 7, F.Weak(1244, "m_pFace", 4,4));
        if (mode == "missing-surface") graph = graph with { FaceSurfaces = graph.FaceSurfaces.Skip(1).ToArray() };
        if (mode == "no-direct-body") graph = F.Replace(graph, -1, Pointers(2009, "m_subNodes"));
        if (mode == "two-local-bodies")
        {
            graph = WallLocalGeometryTests.Graph();
            Assert.Equal(3, SelectOwnWallLocalGeometryInputs(graph, out _)!.Geometries.Count);
        }
        var body = GRepParserV2.SelectOwnFirstDirectPlanarBody(graph, out var failure);
        if (mode != null) { Assert.Null(body); Assert.NotNull(failure); return; }
        Assert.Null(failure); Assert.NotNull(body); Assert.True(body.PreserveSourceFaceIdentity);
        Assert.Equal(6, body.Faces.Count);
        Assert.Equal(new double[] {0,0,0,1,1,1}, body.BoundingBox);
        Assert.True(Assert.Single(body.Solids).IsClosed);
        Assert.Equal(Enumerable.Range(0,6), body.Solids[0].FaceIndices.Order());
    }

    private static OwnGElementGeometryGraph CubeGraph()
    {
        double[][] p = [[0,0,0],[1,0,0],[1,1,0],[0,1,0],[0,0,1],[1,0,1],[1,1,1],[0,1,1]];
        int[][] rings = [[0,3,2,1],[4,5,6,7],[0,1,5,4],[3,7,6,2],[0,4,7,3],[1,2,6,5]];
        double[][] uv = [[0,0],[1,0],[1,1],[0,1]];
        var edges = new List<(int A,int B,List<(int Face,int Index)> Uses)>();
        int[][] faceEdges = rings.Select(_ => new int[4]).ToArray();
        for (int f=0; f<6; f++) for (int i=0; i<4; i++)
        {
            int a=rings[f][i], b=rings[f][(i+1)%4];
            int e=edges.FindIndex(e => e.A==a && e.B==b || e.A==b && e.B==a);
            if (e<0) { e=edges.Count; edges.Add((a,b,[])); }
            edges[e].Uses.Add((f,i)); faceEdges[f][i]=7+e;
        }
        var nodes = new List<GElementGraphNode> {
            Node(-1,2007,Pointers(2009,"m_subNodes",(0,2092))),
            Node(0,2092,Pointers(1978,"m_pFaces",Enumerable.Range(1,6).Select(q => (q,(ushort)1589)).ToArray()),
                Pointers(2092,"m_pEdges",Enumerable.Range(7,12).Select(q => (q,(ushort)1243)).ToArray())) };
        for (int f=0; f<6; f++) nodes.Add(Node(f+1,1589,
            Pointers(1590,"m_faceRegions"),F.Owned(1590,"m_pFirstLoop",(19+2*f,1254,Key(19+2*f))),
            F.Owned(1589,"m_pSurf",(20+2*f,522,Key(20+2*f)))));
        for (int e=0; e<edges.Count; e++)
        {
            var edge=edges[e]; Assert.Equal(2,edge.Uses.Count);
            uint[] Next(bool previous) => edge.Uses.Select(use => {
                int i=use.Index+(previous?-1:1);
                return Key(i<0 || i==4 ? 19+2*use.Face : faceEdges[use.Face][i]); }).ToArray();
            GElementGraphItem Point(int vertex,int offset) => new(offset,offset+32,[],null,
                [new(1248,"uv",2,offset,offset+32,edge.Uses.Select((use,side) =>
                    new GElementGraphItem(offset+16*side,offset+16*(side+1),
                        Bits(uv[System.Array.IndexOf(rings[use.Face],vertex)]),null,[])).ToArray())]);
            nodes.Add(Node(7+e,1243,F.Weak(1244,"m_pFace",edge.Uses.Select(u => Key(u.Face+1)).ToArray()),
                F.Weak(1244,"m_next",Next(false)),F.Weak(1244,"m_prev",Next(true)),Scalar(1244,"m_flags",6),
                new(1244,"m_firstAndLastEdgePnts",2,100,164,[Point(edge.A,100),Point(edge.B,132)]),
                new(1244,"m_interiorEdgePnts",0,164,168,[])));
        }
        for (int f=0; f<6; f++)
        {
            nodes.Add(Node(19+2*f,1254,F.Owned(1255,"m_nextLoop",null),F.Weak(1255,"m_pFace",Key(f+1)),
                F.Weak(1255,"m_next",Key(faceEdges[f][0])),F.Weak(1255,"m_prev",Key(faceEdges[f][3])),
                Scalar(1255,"m_open",0),new(1255,"m_Envelope",null,100,132,
                    [new(100,132,[],null,[new(524,"m_corners",2,100,132,
                        [new(100,116,Bits(0,0),null,[]),new(116,132,Bits(1,1),null,[])])])])));
            double[] origin=p[rings[f][0]];
            nodes.Add(Node(20+2*f,522,Vector("m_origin",origin),
                Vector("m_xVec",p[rings[f][1]].Zip(origin,(a,b)=>a-b).ToArray()),
                Vector("m_yVec",p[rings[f][3]].Zip(origin,(a,b)=>a-b).ToArray())));
        }
        return new(1000,0,nodes,Enumerable.Range(0,6).Select(f => new GElementFaceSurfaceLink(f+1,20+2*f)).ToArray(),10000);
    }

    private static uint Key(int q)=>(uint)q+3;
    private static long[] Bits(params double[] values)=>values.Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static GElementGraphNode Node(int q,ushort cls,params GElementGraphField[] fields)=>
        new(q,cls,q==-1?1000:Key(q),100,5000,[new(1246,"m_GInfo",null,100,116,
            [new(100,116,[-1,0,0,0],null,[])]),..fields]);
    private static GElementGraphField Scalar(int cls,string name,long value)=>
        new(cls,name,null,100,101,[new(100,101,[value],null,[])]);
    private static GElementGraphField Pointers(int cls,string name,params (int Q,ushort C)[] pointers)=>
        new(cls,name,(uint)pointers.Length,100,200,pointers.Select((p,i)=>
            new GElementGraphItem(100+6*i,106+6*i,[],new(Key(p.Q),p.C,p.Q,null,[],100+6*i,106+6*i),[])).ToArray());
    private static GElementGraphField Vector(string name,double[] values)=>new(522,name,3,100,124,
        values.Select((v,i)=>new GElementGraphItem(100+8*i,108+8*i,Bits(v),null,[])).ToArray());
}
