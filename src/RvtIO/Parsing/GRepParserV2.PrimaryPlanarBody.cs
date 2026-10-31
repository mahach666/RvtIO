using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    internal static GRepData? SelectOwnFirstDirectPlanarBody(
        TfBlockDeserializer.OwnGElementGeometryGraph graph, out string? failure)
    {
        var local = TfBlockDeserializer.SelectOwnWallLocalGeometryInputs(graph, out failure);
        if (local == null) return null;
        failure = "primary-planar-local-scope";
        if (local.Geometries.Count != 1 || local.FirstDirectGeometryQueueIndex != local.Geometries[0].QueueIndex)
            return null;
        var primary = local.Geometries[0];
        var nodes = graph.Nodes.ToDictionary(node => node.QueueIndex);
        var faceKeys = primary.Faces.Select(face => face.FaceQueueIndex).ToHashSet();
        if (faceKeys.Count != primary.Faces.Count) return null;
        var edgeKeys = primary.EdgeQueueIndices.Where(q => q != null).Select(q => q!.Value).ToHashSet();
        if (edgeKeys.Any(q => nodes[q].ClassId != 1243)) return null;
        var regions = SelectOwnPlanarFaceRegions(graph, out failure);
        if (regions == null) return null;
        var topology = TfBlockDeserializer.SelectOwnGElementLoopTopology(graph, out failure);
        if (topology == null) return null;
        failure = "primary-planar-edge-incidence";
        var visits = new Dictionary<int, List<(int Face, int? Side)>>();
        foreach (var face in topology.Faces.Where(face => faceKeys.Contains(face.FaceQueueIndex)))
            foreach (var loop in face.Loops)
                foreach (var edge in loop.Edges)
                {
                    if (!edgeKeys.Contains(edge.EdgeQueueIndex)) return null;
                    if (!visits.TryGetValue(edge.EdgeQueueIndex, out var uses))
                        visits.Add(edge.EdgeQueueIndex, uses = []);
                    uses.Add((face.FaceQueueIndex, edge.FaceSide));
                }
        if (visits.Count == 0 || visits.Values.Any(uses => uses.Count != 2
            || !(uses[0].Side == 0 && uses[1].Side == 1 || uses[0].Side == 1 && uses[1].Side == 0))) return null;
        var faces = primary.Faces.SelectMany(face => regions.GetValueOrDefault(face.FaceQueueIndex) ?? []).ToList();
        // Frame-local queue identity, never aliases between equal serialized handles.
        var identities = primary.Faces.SelectMany(face => Enumerable.Repeat((uint)face.FaceQueueIndex + 1,
            regions.GetValueOrDefault(face.FaceQueueIndex)?.Count ?? 0)).ToArray();
        faces = faces.Select((region, index) => region with { SourceFace = identities[index] }).ToList();
        var solids = BuildTopologicalSolids(faces, identities,
            visits.Values.Select(uses => ((uint)uses[0].Face + 1, (uint)uses[1].Face + 1)));
        failure = "primary-planar-topology-coverage";
        if (faces.Count == 0 || solids.Count == 0 || solids.Any(solid => !solid.IsClosed)
            || solids.Sum(solid => solid.FaceIndices.Count) != faces.Count
            || solids.SelectMany(solid => solid.FaceIndices).Distinct().Count() != faces.Count) return null;
        var points = faces.SelectMany(face => face.Vertices!.Concat((face.InnerVertices ?? []).SelectMany(ring => ring))).ToArray();
        var bounds = Enumerable.Range(0, 3).Select(axis => points.Min(point => point[axis]))
            .Concat(Enumerable.Range(0, 3).Select(axis => points.Max(point => point[axis]))).ToArray();
        failure = null;
        return new GRepData { Faces = faces, Solids = solids, BoundingBox = bounds, PreserveSourceFaceIdentity = true };
    }
}
