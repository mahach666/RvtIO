namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    /// <summary>The export-id rule: expands filters only and accepts one direct Geometry with
    /// active visible parent faces. Region identities remain scoped to their owning face.</summary>
    internal static IReadOnlyDictionary<uint, uint>? SelectOwnPostCutFaceGroups(OwnGElementGeometryGraph graph,
        IReadOnlyCollection<uint> usedFaces)
    {
        var local = SelectOwnWallLocalGeometryInputs(graph, out _);
        if (local?.Geometries is not [var body] || local.FirstDirectGeometryQueueIndex != body.QueueIndex
            || body.Faces.Count == 0 || usedFaces.Count == 0) return null;
        var groups = body.Faces.ToDictionary(face => (uint)face.FaceQueueIndex + 1,
            face => (uint)(face.RegionOfQueueIndex ?? face.FaceQueueIndex) + 1);
        if (usedFaces.Any(face => !groups.ContainsKey(face))) return null;
        var parents = usedFaces.Select(face => groups[face]).ToHashSet();
        if (body.Faces.Where(face => parents.Contains((uint)face.FaceQueueIndex + 1))
            .Any(face => (face.Flags & 0x80000) == 0 || (face.Flags & 0x1e0) != 0)) return null;
        var solids = new List<int>();
        var seen = new HashSet<int>();
        bool Children(GElementGraphNode node)
        {
            var fields = node.Fields.Where(field => field.Declaring == 2009 && field.Name == "m_subNodes").ToArray();
            if (fields is not [var children]) return false;
            foreach (var item in children.Items)
            {
                if (item.Pointer?.Handle == 0) continue;
                if (item.Pointer?.QueueIndex is not int index || index < 0 || index + 1 >= graph.Nodes.Count
                    || !seen.Add(index)) return false;
                var child = graph.Nodes[index + 1];
                if (child.ClassId != item.Pointer.ClassId) return false;
                if (child.ClassId == 2015) { if (!Children(child)) return false; }
                else if (child.ClassId == 2092) solids.Add(index);
                else if (child.ClassId is not (2009 or 1736 or 1974 or 1991 or 1996 or 2014)) return false;
            }
            return true;
        }
        if (!Children(graph.Nodes[0]) || solids.Count != 1 || solids[0] != body.QueueIndex) return null;
        return groups;
    }
}
