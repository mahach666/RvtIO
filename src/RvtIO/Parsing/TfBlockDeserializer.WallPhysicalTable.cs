namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallTableNode(string Archive, int QueueIndex, ushort ClassId,
        int Tag, uint Flags, int Start, int End);
    internal sealed record WallPhysicalNodeVisit(string Kind, int GeometryQueueIndex, int ArrayIndex,
        int? SourceQueueIndex, int? RegionOfQueueIndex, WallTableNode? Node,
        WallTableNode? Previous, string Outcome);
    internal sealed record WallPhysicalTableSlot(WallLogicalGeomSlot Persisted, WallTableNode? Node);
    internal sealed record OwnWallPhysicalTablePrefix(int OwnerId, ushort WallClassId, int MaxId,
        int GeometryCount, int? FirstDirectGeometryQueueIndex, IReadOnlyList<WallPhysicalNodeVisit> Visits,
        IReadOnlyList<WallPhysicalTableSlot> Slots);

    /// <summary>Original physical face/edge updates AFTER callback36, for the own
    /// instance-wall scope (not generic ElementType owners). Inputs must be an
    /// explicitly supplied captured GRep: matching IDs do not confirm current-record
    /// selection. Qualified Wall/GRep identities never alias by handle, tag or shape.
    /// Stops BEFORE callback37 clearing; not a final/current/active table.</summary>
    internal static OwnWallPhysicalTablePrefix? ApplyOwnWallPhysicalTablePrefix(ushort wallClassId,
        OwnWallNonBRepTablePrefix prefix, OwnGElementGeometryGraph graph, out string? failure)
    {
        failure = "physical-owner";
        if (wallClassId is not (3524 or 361) || prefix.OwnerId <= 0 || prefix.OwnerId != graph.OwnerId
            || prefix.MaxId < -1 || prefix.MaxId > 100_000 || prefix.Slots.Count != prefix.MaxId + 1
            || prefix.Slots.Where((s, i) => s.Persisted.Id != i || s.Node != null && s.Node.Tag != i).Any()
            || !HasGElementGraphSchemas()) return null;
        var local = SelectOwnWallLocalGeometryInputs(graph, out failure);
        if (local == null) return null;
        var current = prefix.Slots.Select(s => s.Node == null ? null : new WallTableNode("Wall",
            s.Node.QueueIndex, s.Node.ClassId, s.Node.Tag, s.Node.Flags, s.Node.Start, s.Node.End)).ToArray();
        var visits = new List<WallPhysicalNodeVisit>();
        string context = "physical";
        try
        {
            foreach (var geometry in local.Geometries)
            {
                bool primary = geometry.QueueIndex == local.FirstDirectGeometryQueueIndex;
                for (int index = 0; index < geometry.Faces.Count; index++)
                {
                    var face = geometry.Faces[index];
                    context = $"physical-face:{geometry.QueueIndex}:{face.FaceQueueIndex}";
                    var node = Node(face.FaceQueueIndex);
                    if (node.ClassId != 1589) throw Fail;
                    bool inRange = node.Tag >= 0 && node.Tag <= prefix.MaxId;
                    var previous = inRange ? current[node.Tag] : null;
                    string outcome = !inRange ? "out-of-range" : previous == null || primary ? "write" : "keep-non-primary";
                    if (outcome == "write") current[node.Tag] = node;
                    visits.Add(new("Face", geometry.QueueIndex, index, face.FaceQueueIndex,
                        face.RegionOfQueueIndex, node, previous, outcome));
                }
                for (int index = 0; index < geometry.EdgeQueueIndices.Count; index++)
                {
                    int? queueIndex = geometry.EdgeQueueIndices[index];
                    context = $"physical-edge:{geometry.QueueIndex}:{queueIndex}";
                    if (queueIndex == null)
                    { visits.Add(new("Edge", geometry.QueueIndex, index, null, null, null, null, "null")); continue; }
                    var source = graph.Nodes[queueIndex.Value + 1];
                    // Native isKindOf(GEdge) happens BEFORE reading the tag.
                    if (!Kind(source.ClassId, 1244))
                    { visits.Add(new("Edge", geometry.QueueIndex, index, queueIndex, null, null, null, "not-gedge")); continue; }
                    var node = Node(queueIndex.Value);
                    bool inRange = node.Tag >= 0 && node.Tag <= prefix.MaxId;
                    var previous = inRange ? current[node.Tag] : null;
                    string outcome;
                    if (!inRange) outcome = "out-of-range";
                    else if (previous == null) outcome = "write";
                    else if (local.Geometries.Count >= 2) outcome = primary ? "write" : "keep-non-primary";
                    else if (Kind(previous.ClassId, 1243)) outcome = "keep-existing-edge";
                    else outcome = "write-single-non-edge"; // original warns, then writes; own wall is not ElementType
                    if (outcome is "write" or "write-single-non-edge") current[node.Tag] = node;
                    visits.Add(new("Edge", geometry.QueueIndex, index, queueIndex, null, node, previous, outcome));
                }
            }
            failure = null;
            return new(prefix.OwnerId, wallClassId, prefix.MaxId, local.Geometries.Count,
                local.FirstDirectGeometryQueueIndex, visits,
                prefix.Slots.Select((s, i) => new WallPhysicalTableSlot(s.Persisted, current[i])).ToArray());
        }
        catch (DecodeFail) { failure = context; return null; }

        static bool Kind(ushort cls, ushort parent)
        {
            while (true)
            {
                if (cls == parent) return true;
                if (!GElementGraphSchemas.TryGetValue(cls, out var schema)) throw Fail;
                if (schema.Base is not int next) return false;
                cls = (ushort)next;
            }
        }
        WallTableNode Node(int index)
        {
            var node = graph.Nodes[index + 1];
            var fields = node.Fields.Where(f => f.Declaring == 1246 && f.Name == "m_GInfo").ToArray();
            if (fields.Length != 1 || fields[0].Items.Count != 1 || fields[0].Items[0].Values.Count != 4) throw Fail;
            var info = fields[0].Items[0].Values;
            if (info[0] < int.MinValue || info[0] > int.MaxValue || info[3] < 0 || info[3] > uint.MaxValue) throw Fail;
            return new("GRep", index, node.ClassId, (int)info[0], (uint)info[3], node.Start, node.End);
        }
    }
}
