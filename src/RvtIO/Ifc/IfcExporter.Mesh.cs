using RvtIO.Geometry.Meshing;
using RvtIO.Model;
using RvtIO.Parsing;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>Level of detail of the face meshes for the export level (Low, the default, is 0.2).</summary>
    private static readonly double MeshLod = Environment.GetEnvironmentVariable("RVTIO_X_LOD") is { } xl ? double.Parse(xl, System.Globalization.CultureInfo.InvariantCulture) : 0.2;

    /// <summary>Axis-wise tolerance of the vertex comparator of the triangle sets, in feet.</summary>
    private const double MeshVertexTolerance = 0.0005233832795;

    private (StepWriter Writer, int Id)? _meshGlobalOrigin;

    private static readonly bool MeshLogOn = Environment.GetEnvironmentVariable("RVTIO_MESH_LOG") == "1";
    private static void MeshLog(string message) { if (MeshLogOn) DiagnosticSink.WriteLine("[meshmesh] " + message); }

    /// <summary>Writes the Geometry nodes exported as triangle sets: one IfcFacetedBrep per node holding one IfcFace per triangle of the
    /// face meshes, the vertices shared by a tolerant per-node cache, nothing dropped or reordered. A node whose
    /// faces are not all there (or do not mesh) is left to the dense polygons. Returns the nodes written.</summary>
    private HashSet<int> WriteTriangleNodes(StepWriter w, GeometryData geo, double offX, double offY, double offZ,
        Func<double[], double[]>? localFrame, List<(long Order, int Node, int BrepId)> items)
    {
        var written = new HashSet<int>();
        if (!CurrentProfile || !GRepParserV2.MeshEnabled || geo.MeshNodes is not { Count: > 0 } nodes)
        {
            if (MeshLogOn && CurrentProfile && geo.Faces.Count > 200)
                MeshLog($"geometry without the reference nodes: {geo.Faces.Count} faces, nodes {string.Join(" ", geo.Faces.GroupBy(f => f.GeometryNode).OrderBy(g => g.Key).Take(12).Select(g => $"{g.Key}:{g.Count()}"))}, meshNodes={(geo.MeshNodes == null ? "null" : "0")}");
            return written;
        }
        if (MeshLogOn)
            MeshLog("geometry: nodes " + string.Join(" ", geo.Faces.GroupBy(f => f.GeometryNode).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))
                + $" ghost={geo.GhostNodes?.Count ?? 0} decoded=" + string.Join(",", nodes.Select(n => $"{n.Node}:{n.Faces.Count}")));
        foreach (var node in nodes)
        {
            var nodeFaces = geo.Faces.Where(face => face.GeometryNode == node.Node).ToList();
            if (!node.Ghost && (nodeFaces.Count == 0 || nodeFaces.Any(face => (face.SourceFace == 0 && face.MeshMarker == 0)
                || !node.Faces.Any(meshFace => (uint)meshFace.Marker == (face.SourceFace != 0 ? face.SourceFace : face.MeshMarker)))))
            {
                MeshLog($"node {node.Node} faces {node.Faces.Count} geo {nodeFaces.Count}: a polygon face is not a decoded face");
                continue;
            }
            var meshes = new List<Mesh>();
            bool meshed = true;
            // the mesh is made in the frame of the symbol the node comes from; the instance transform puts it where the polygons are
            var placement = node.Placement;
            // a mirror image keeps the order of the triangle corners: the triangles are turned inside out
            double[] Place(double[] p) => placement == null ? p : new[]
            {
                placement[9] + p[0] * placement[0] + p[1] * placement[1] + p[2] * placement[2],
                placement[10] + p[0] * placement[3] + p[1] * placement[4] + p[2] * placement[5],
                placement[11] + p[0] * placement[6] + p[1] * placement[7] + p[2] * placement[8],
            };
            // the free form faces of a node are meshed together: their edges are sampled once for every face they bound
            var freeformSolid = node.Faces.Any(FreeformSolid.IsFreeform) ? FreeformSolid.Create(node.Faces, MeshLod, node.Diagonal) : null;
            if (node.Faces.Any(FreeformSolid.IsFreeform) && freeformSolid == null) { MeshLog($"node {node.Node}: the free form faces are not covered"); continue; }
            foreach (var face in node.Faces)
            {
                Mesh? mesh;
                bool ok;
                if (FreeformSolid.IsFreeform(face)) ok = freeformSolid!.TryMesh(face, out mesh);
                else ok = FaceMesher.TryMesh(GRepParserV2.WithSamples(face, MeshLod, node.Diagonal, freeformSolid), MeshLod, node.Diagonal, out mesh);
                if (!ok) { MeshLog($"node {node.Node}: {face.SurfaceKind} face {face.Marker} does not mesh [{(FreeformSolid.IsFreeform(face) ? FreeformSolid.LastRefusal : RevolvedMesher.LastRefusal)}] ({face.Loops.Count} loops, {face.Loops.Sum(l => l.Coedges.Count)} coedges, {string.Join("/", face.Loops.SelectMany(l => l.Coedges).Select(ce => ce.Curve!.Kind.ToString()).Distinct())})"); meshed = false; break; }
                meshes.Add(mesh!);
            }
            if (!meshed || meshes.Sum(mesh => mesh.Triangles.Length) == 0) { MeshLog($"node {node.Node}: a face does not mesh"); continue; }

            // the mesh lives in the frame of the geometry: it has to coincide with the polygons the node was decoded with
            double[] meshMin = [double.MaxValue, double.MaxValue, double.MaxValue], meshMax = [double.MinValue, double.MinValue, double.MinValue];
            double[] polyMin = [double.MaxValue, double.MaxValue, double.MaxValue], polyMax = [double.MinValue, double.MinValue, double.MinValue];
            foreach (var vertex in meshes.SelectMany(mesh => mesh.Vertices).Select(Place))
                for (int k = 0; k < 3; k++) { meshMin[k] = Math.Min(meshMin[k], vertex[k]); meshMax[k] = Math.Max(meshMax[k], vertex[k]); }
            foreach (var vertex in nodeFaces.Where(face => face.OuterVertices != null).SelectMany(face => face.OuterVertices!))
                for (int k = 0; k < 3; k++) { polyMin[k] = Math.Min(polyMin[k], vertex[k]); polyMax[k] = Math.Max(polyMax[k], vertex[k]); }
            if (!node.Ghost && Enumerable.Range(0, 3).Any(k => Math.Abs(meshMin[k] - polyMin[k]) > 0.03 || Math.Abs(meshMax[k] - polyMax[k]) > 0.03))
            {
                MeshLog($"node {node.Node}: mesh box differs from the polygons (min {string.Join(",", Enumerable.Range(0, 3).Select(k => (meshMin[k] - polyMin[k]).ToString("G3")))}"
                    + $" max {string.Join(",", Enumerable.Range(0, 3).Select(k => (meshMax[k] - polyMax[k]).ToString("G3")))}; kinds {string.Join("/", node.Faces.Select(f => f.SurfaceKind.ToString()).Distinct())})");
                continue;
            }

            var PointId = CreatePointCache(w, localFrame, offX, offY, offZ);

            var faceIds = new List<int>();
            foreach (var mesh in meshes)
            {
                var ids = mesh.Vertices.Select(v => PointId(Place(v))).ToArray();
                foreach (var triangle in mesh.Triangles)
                {
                    int loopId = w.Add("IFCPOLYLOOP", StepWriter.List(triangle.Select(index => R(ids[index]))));
                    int boundId = w.Add("IFCFACEOUTERBOUND", $"{R(loopId)},.T.");
                    faceIds.Add(w.Add("IFCFACE", StepWriter.List(new[] { R(boundId) })));
                }
            }
            int shellId = w.Add("IFCCLOSEDSHELL", StepWriter.List(faceIds.Select(R)));
            int brepId = w.Add("IFCFACETEDBREP", R(shellId));
            _bodyItemOrigins[brepId] = (geo, node.Node);
            items.Add((node.NodeOrder != 0 ? node.NodeOrder : long.MaxValue, node.Node, brepId));
            written.Add(node.Node);
            MeshLog($"node {node.Node}: {faceIds.Count} triangles from {meshes.Count} faces (dense {nodeFaces.Count})");
        }
        return written;
    }

    /// <summary>The vertex cache of a triangle set or polygon set: a point within the vertex tolerance on every axis of
    /// one written before is that one (the first wins); a new point is written as millimetres (feet x 304.8, a component within 1e-10 of an integer
    /// snapped to it) and the points of the origin share one IfcCartesianPoint. Returns the id of the point for a raw point in feet.</summary>
    private Func<double[], int> CreatePointCache(StepWriter w, Func<double[], double[]>? localFrame, double offX, double offY, double offZ)
    {
        var cache = new Dictionary<(long, long, long), List<(double[] Point, int Id)>>();
        return raw =>
        {
            long cx = (long)Math.Floor(raw[0] / MeshVertexTolerance), cy = (long)Math.Floor(raw[1] / MeshVertexTolerance), cz = (long)Math.Floor(raw[2] / MeshVertexTolerance);
            int found = -1;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    for (long dz = -1; dz <= 1; dz++)
                        if (cache.TryGetValue((cx + dx, cy + dy, cz + dz), out var cell))
                            foreach (var (point, id) in cell)
                                if (Math.Abs(point[0] - raw[0]) <= MeshVertexTolerance && Math.Abs(point[1] - raw[1]) <= MeshVertexTolerance
                                    && Math.Abs(point[2] - raw[2]) <= MeshVertexTolerance && (found < 0 || id < found))
                                    found = id;
            if (found >= 0) return found;
            var placed = localFrame != null ? localFrame(raw) : raw;
            double[] mm = [(placed[0] + offX) * 304.8, (placed[1] + offY) * 304.8, (placed[2] + offZ) * 304.8];
            for (int k = 0; k < 3; k++)
            {
                double nearest = Math.Round(mm[k]);
                if (Math.Abs(mm[k] - nearest) < 1e-10) mm[k] = nearest;
            }
            int pointId;
            if (Math.Abs(mm[0]) < 0.001 && Math.Abs(mm[1]) < 0.001 && Math.Abs(mm[2]) < 0.001)
            {
                if (_meshGlobalOrigin is not { } origin || !ReferenceEquals(origin.Writer, w))
                    _meshGlobalOrigin = (w, w.Add("IFCCARTESIANPOINT", StepWriter.List(D(0), D(0), D(0))));
                pointId = _meshGlobalOrigin.Value.Id;
            }
            else
                pointId = w.Add("IFCCARTESIANPOINT", StepWriter.List(
                    StepWriter.RealFromMillimetres(mm[0]), StepWriter.RealFromMillimetres(mm[1]), StepWriter.RealFromMillimetres(mm[2])));
            var key = (cx, cy, cz);
            if (!cache.TryGetValue(key, out var list)) cache[key] = list = new();
            list.Add((raw, pointId));
            return pointId;
        };
    }
}
