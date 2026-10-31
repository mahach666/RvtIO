namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallLocalFaceInput(int GeometryQueueIndex, int FaceQueueIndex,
        int? RegionOfQueueIndex, int Tag, uint Flags, GElementGraphPointer SurfacePointer);
    internal sealed record WallLocalGeometryInput(int QueueIndex,
        IReadOnlyList<WallLocalFaceInput> Faces, IReadOnlyList<int?> EdgeQueueIndices);
    internal sealed record OwnWallLocalGeometryInputs(int OwnerId, int? FirstDirectGeometryQueueIndex,
        IReadOnlyList<int> VisitedGroupQueueIndices, IReadOnlyList<int> SkippedNodeQueueIndices,
        IReadOnlyList<WallLocalGeometryInput> Geometries);

    /// <summary>The parseForLocalGeometry traversal (null test data) used by the wall-table rebuild. GBReps are included
    /// without descending; only untagged groups are traversed. Face iteration includes one level of regions without
    /// testing flag 19. These are reconstruction inputs, not a completed table, a view/body selection or an
    /// active-face/extrusion result.</summary>
    internal static OwnWallLocalGeometryInputs? SelectOwnWallLocalGeometryInputs(
        OwnGElementGeometryGraph graph, out string? failure)
    {
        failure = "graph";
        if (graph.OwnerId <= 0 || graph.Nodes.Count == 0 || graph.Nodes[0].ClassId != 2007
            || graph.Nodes.Where((n, i) => n.QueueIndex != i - 1).Any()) return null;
        var nodes = graph.Nodes;
        var groups = new List<int>();
        var skipped = new List<int>();
        var geometries = new List<WallLocalGeometryInput>();
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        string context = "root";
        try
        {
            int? firstDirect = null;
            var rootChildren = Children(nodes[0]);
            foreach (int child in rootChildren)
                if (nodes[child + 1].ClassId == 2092) { firstDirect = child; break; }
            groups.Add(-1);
            foreach (int child in Enumerable.Reverse(rootChildren)) pending.Push(child);
            while (pending.TryPop(out int index))
            {
                context = $"visit:{index}";
                if (!seen.Add(index)) throw Fail;
                var node = nodes[index + 1];
                // The closure returns 0 for a GBRep before inspecting its tag.
                if (node.ClassId == 2092)
                {
                    var faces = new List<WallLocalFaceInput>();
                    foreach (var item in Field(node, 1978, "m_pFaces").Items)
                    {
                        int face = Owned(item.Pointer, false)!.Value;
                        Face(face, null);
                        // GFaceIter keeps one saved top-level range: it does
                        // not recursively expand regions of a region.
                        foreach (var region in Field(nodes[face + 1], 1590, "m_faceRegions").Items)
                            Face(Owned(region.Pointer, false)!.Value, face);
                    }
                    var edges = Field(node, 2092, "m_pEdges").Items
                        .Select(item => Owned(item.Pointer, true)).ToArray();
                    geometries.Add(new(index, faces, edges));
                    continue;

                    void Face(int face, int? regionOf)
                    {
                        context = $"face:{index}:{face}";
                        var entry = nodes[face + 1];
                        if (entry.ClassId != 1589) throw Fail;
                        var info = Info(entry);
                        var surface = Field(entry, 1589, "m_pSurf").Items.Single().Pointer!;
                        faces.Add(new(index, face, regionOf, (int)info[0], (uint)info[3], surface));
                    }
                }
                // The closure returns -1 for any tag other than -1. It does not
                // use serialized handles, node flags, condition values or shape.
                if (Info(node)[0] != -1) { skipped.Add(index); continue; }
                if (node.ClassId is 2007 or 2009 or 2015)
                {
                    groups.Add(index);
                    foreach (int child in Enumerable.Reverse(Children(node))) pending.Push(child);
                }
                else if (node.ClassId is not (1991 or 1736 or 1974)) // source-bound GNode child-count0
                { context = $"untagged-child-count:{index}:{node.ClassId}"; throw Fail; }
            }
            failure = null;
            return new(graph.OwnerId, firstDirect, groups, skipped, geometries);
        }
        catch (DecodeFail) { failure = context; return null; }

        GElementGraphField Field(GElementGraphNode node, int declaring, string name)
        {
            var matches = node.Fields.Where(f => f.Declaring == declaring && f.Name == name).ToArray();
            if (matches.Length != 1) throw Fail;
            return matches[0];
        }
        IReadOnlyList<long> Info(GElementGraphNode node)
        {
            var items = Field(node, 1246, "m_GInfo").Items;
            if (items.Count != 1 || items[0].Values.Count != 4) throw Fail;
            return items[0].Values;
        }
        int? Owned(GElementGraphPointer? pointer, bool allowNull)
        {
            if (pointer == null) throw Fail;
            if (pointer.Handle == 0 && allowNull) return null;
            if (pointer.QueueIndex is not int index || index < 0 || index >= nodes.Count - 1
                || nodes[index + 1].ClassId != pointer.ClassId) throw Fail;
            return index;
        }
        int[] Children(GElementGraphNode node)
        {
            context = $"children:{node.QueueIndex}";
            return Field(node, 2009, "m_subNodes").Items
                .Select(item => Owned(item.Pointer, true)).Where(i => i.HasValue).Select(i => i!.Value).ToArray();
        }
    }
}
