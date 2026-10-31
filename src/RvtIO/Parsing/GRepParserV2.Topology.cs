using System.Globalization;
using System.Text;
using RvtIO.Geometry.Meshing;
using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    /// <summary>
    /// Diagnostic (RVTIO_TOPO=&lt;file&gt;): one JSON line per body Face with the serialized
    /// topology the Revit face mesher needs: surface, loops in file order, and per coedge the
    /// Edge handle, side.
    /// </summary>
    private static void DumpTopology(Cur c, Dictionary<uint, SurfaceRec> surfaces, IEnumerable<uint> bodyFaces,
        Dictionary<uint, List<int>> loopsByFace, string path)
    {
        var edges = EdgesByHandle(c);
        MeshMarkFreeformEdges(edges, surfaces);
        var bodyFacesSet = bodyFaces.ToHashSet();
        var faceQueueIndex = new Dictionary<uint, int>();
        for (int qi = 0; qi < c.Queue.Count; qi++) if (c.Queue[qi].Cls == 1589 && c.Queue[qi].V > 0) faceQueueIndex.TryAdd((uint)c.Queue[qi].V, qi);
        var faceOrderIndex = new Dictionary<uint, int>();
        for (int fo = 0; fo < c.FaceOrder.Count; fo++) faceOrderIndex.TryAdd(c.FaceOrder[fo], fo);
        var sb = new StringBuilder();
        var built = new Dictionary<uint, BrFace>();
        var diagonalByFace = new Dictionary<uint, double>();
        var dumpNodes = ResolveGeometryNodes(c.Queue, c.QueueLinks, c.ObjectGInfo);
        var dumpSolids = new Dictionary<(int, double), FreeformSolid?>();
        var dumpVertices = MeshVertexModel ? MeshVertexTable.Build(c, edges, surfaces, bodyFacesSet, loopsByFace) : null;
        if (Environment.GetEnvironmentVariable("RVTIO_TOPO_MESH") == "1")
        {
            var nodeByFace = ResolveGeometryNodes(c.Queue, c.QueueLinks, c.ObjectGInfo);
            foreach (uint f in bodyFaces)
                if (surfaces.TryGetValue(f, out var sf) && loopsByFace.TryGetValue(f, out var li)
                    && BuildBrFace(f, sf, MeshFlipped(c, f, sf), li, c, edges, surfaces, bodyFacesSet, dumpVertices) is { } bf)
                    built[f] = bf;
            foreach (var group in built.GroupBy(kv => nodeByFace.GetValueOrDefault(kv.Key).Node))
            {
                var nodeFaces = group.Select(kv => kv.Value).ToList();
                bool freeformNode = nodeFaces.Any(FreeformSolid.IsFreeform);
                if (freeformNode) foreach (var f in nodeFaces) f.Solid = nodeFaces;
                double nodeDiagonal = freeformNode ? FreeformSolid.CurveDiagonal(nodeFaces) : MeshNodeDiagonal(nodeFaces);
                foreach (var kv in group) diagonalByFace[kv.Key] = nodeDiagonal;
            }
        }
        string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        string V(double[] v) => "[" + string.Join(",", v.Select(N)) + "]";
        foreach (uint face in bodyFaces.OrderBy(h => h))
        {
            if (!surfaces.TryGetValue(face, out var s)) continue;
            c.ObjectGInfo.TryGetValue((int)face, out var info);
            sb.Append("{\"eid\":").Append(c.ElementId).Append(",\"face\":").Append(face).Append(",\"fq\":").Append(faceQueueIndex.GetValueOrDefault(face, -1)).Append(",\"fo\":").Append(faceOrderIndex.GetValueOrDefault(face, -1)).Append(",\"node\":").Append(dumpNodes.GetValueOrDefault(face).Node)
              .Append(",\"gflags\":").Append(info.Flags).Append(",\"tag\":").Append(info.Tag).Append(",\"ffl\":").Append(c.FaceFlagsV9.TryGetValue(face, out var ffl) ? (long)ffl : -1)
              .Append(",\"surface\":{\"kind\":\"").Append(s.Kind).Append("\",\"o\":").Append(V(s.O)).Append(",\"x\":").Append(V(s.X))
              .Append(",\"y\":").Append(V(s.Y)).Append(",\"z\":").Append(V(s.Z)).Append(",\"r\":").Append(N(s.R)).Append(",\"orient\":").Append(s.Orient);
            if (s.Env != null) sb.Append(",\"env\":").Append(V(s.Env));
            if (s.Kind == SurfKind.Rev && c.Curves.TryGetValue(s.ProfileHandle, out var dumpProfile) && dumpProfile.NurbControlPoints == null && dumpProfile.HermiteNodes == null)
                sb.Append(",\"profile\":{\"c\":").Append(V(dumpProfile.C)).Append(",\"x\":").Append(V(dumpProfile.X)).Append(",\"y\":").Append(V(dumpProfile.Y))
                  .Append(",\"r\":").Append(N(dumpProfile.R)).Append(",\"ry\":").Append(N(dumpProfile.RY)).Append(",\"t0\":").Append(N(dumpProfile.T0)).Append(",\"t1\":").Append(N(dumpProfile.T1)).Append('}');
            sb.Append("}");
            if (built.TryGetValue(face, out var meshFace))
            {
                double diagonal = diagonalByFace.GetValueOrDefault(face);
                sb.Append(",\"diag\":").Append(N(diagonal)).Append(",\"meshes\":{");
                bool firstLod = true;
                List<string>? knife02 = null;
                foreach (double lod in new[] { 0.1, 0.2, 0.3, 0.4 })
                {
                    RevolvedMesher.Knife = null;
                    FreeformSolid? solid = null;
                    if (meshFace.Solid != null && !dumpSolids.TryGetValue((dumpNodes.GetValueOrDefault(face).Node, lod), out solid))
                        dumpSolids[(dumpNodes.GetValueOrDefault(face).Node, lod)] = solid = FreeformSolid.Create(meshFace.Solid, lod, diagonal);
                    var withSamples = WithSamples(meshFace, lod, diagonal, solid);
                    if (!firstLod) sb.Append(',');
                    firstLod = false;
                    sb.Append('"').Append(N(lod)).Append("\":");
                    Mesh? mesh = null;
                    bool meshOk = solid != null && FreeformSolid.IsFreeform(meshFace) ? solid.TryMesh(meshFace, out mesh) : FaceMesher.TryMesh(withSamples, lod, diagonal, out mesh);
                    if (meshOk)
                        sb.Append("{\"v\":[").Append(string.Join(",", mesh.Vertices.Select(V))).Append("],\"t\":[")
                          .Append(string.Join(",", mesh.Triangles.Select(t => "[" + string.Join(",", t) + "]"))).Append("]}");
                    else sb.Append("null");
                    if (Math.Abs(lod - 0.2) < 1e-9) knife02 = RevolvedMesher.Knife;
                }
                sb.Append('}');
                if (knife02 != null) sb.Append(",\"knife\":[").Append(string.Join(",", knife02.Select(k => "\"" + k + "\""))).Append(']');
            }
            if (built.TryGetValue(face, out var ceFace))
            {
                // the decoded coedges (B-rep ends and the arc handed to the sampler), for bit
                // comparison with the Revit edges
                sb.Append(",\"ce\":[");
                bool firstCe = true;
                foreach (var ce in ceFace.Loops.SelectMany(l => l.Coedges))
                {
                    if (!firstCe) sb.Append(',');
                    firstCe = false;
                    sb.Append("{\"m\":").Append(ce.EdgeMarker).Append(",\"fwd\":").Append(ce.OrientToLoop ? "true" : "false")
                      .Append(",\"v1\":").Append(ce.V1 == null ? "null" : V(ce.V1)).Append(",\"v2\":").Append(ce.V2 == null ? "null" : V(ce.V2))
                      .Append(",\"kind\":\"").Append(ce.Curve?.Kind).Append('"');
                    if (ce.Curve?.Arc is { } ceArc)
                        sb.Append(",\"arc\":{\"c\":").Append(V(ceArc.Center)).Append(",\"r\":").Append(N(ceArc.Radius)).Append(",\"n\":").Append(V(ceArc.Normal))
                          .Append(",\"ref\":").Append(V(ceArc.Ref)).Append(",\"s\":").Append(N(ceArc.Start)).Append(",\"e\":").Append(N(ceArc.End))
                          .Append(ceArc.EdgeRange is { } er ? ",\"er\":[" + N(er.From) + "," + N(er.To) + "]" : "").Append('}');
                    sb.Append('}');
                }
                sb.Append(']');
            }
            sb.Append(",\"loops\":[");
            if (!loopsByFace.TryGetValue(face, out var indices)) { sb.Append("]}\n"); continue; }
            bool firstLoop = true;
            foreach (int index in indices)
            {
                if (!firstLoop) sb.Append(',');
                firstLoop = false;
                var loop = c.SpaceEdgeLoops[index];
                uint sentinel = (uint)c.Queue[index].V, current = loop.Next;
                sb.Append("{\"open\":").Append(loop.Open).Append(",\"coedges\":[");
                bool first = true;
                int guard = 0;
                while (current != sentinel && guard++ < 100000)
                {
                    if (!edges.TryGetValue(current, out var e) || e == null) break;
                    bool sideA = e.FaceA == face;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"edge\":").Append(e.Handle).Append(",\"etag\":").Append(c.ObjectGInfo.TryGetValue((int)e.Handle, out var einfo) ? einfo.Tag : -999).Append(",\"q\":").Append(e.QueueIndex).Append(",\"fa\":").Append(e.FaceA).Append(",\"fb\":").Append(e.FaceB).Append(",\"nx\":").Append(sideA ? e.NextA : e.NextB).Append(",\"pv\":").Append(sideA ? e.PrevA : e.PrevB).Append(",\"sideA\":").Append(sideA ? "true" : "false")
                      .Append(",\"flags\":").Append(e.Flags).Append(",\"endpoints\":").Append(e.HasEndpoints ? "true" : "false")
                      .Append(",\"other\":").Append(sideA ? e.FaceB : e.FaceA).Append(",\"uv\":[");
                    var pts = e.AuthoredPoints();
                    for (int i = 0; i < pts.Count; i++)
                    {
                        double u = sideA ? pts[i].UA : pts[i].UB, v = sideA ? pts[i].VA : pts[i].VB;
                        if (i > 0) sb.Append(',');
                        sb.Append('[').Append(N(u)).Append(',').Append(N(v)).Append(']');
                    }
                    sb.Append(']');
                    if (s.Kind is SurfKind.Plane or SurfKind.Cylinder)
                    {
                        sb.Append(",\"xyz\":[");
                        for (int i = 0; i < pts.Count; i++)
                        {
                            double u = sideA ? pts[i].UA : pts[i].UB, v = sideA ? pts[i].VA : pts[i].VB;
                            double x = s.Kind == SurfKind.Cylinder ? s.R * Math.Cos(u) : u;
                            double y = s.Kind == SurfKind.Cylinder ? s.R * Math.Sin(u) : v;
                            double z = s.Kind == SurfKind.Cylinder ? v : 0;
                            var w = Enumerable.Range(0, 3).Select(k => s.O[k] + x * s.X[k] + y * s.Y[k] + z * s.Z[k]).ToArray();
                            if (i > 0) sb.Append(',');
                            sb.Append(V(w));
                        }
                        sb.Append(']');
                    }
                    sb.Append('}');
                    current = sideA ? e.NextA : e.NextB;
                }
                sb.Append("]}");
            }
            sb.Append("]}\n");
        }
        lock (MeshTopologyLock) File.AppendAllText(path, sb.ToString());
    }

    private static readonly object MeshTopologyLock = new();
}
