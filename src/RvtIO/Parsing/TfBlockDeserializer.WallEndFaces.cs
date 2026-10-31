using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    /// <summary>The face-history table of the first BaseWallGStep (500) of the own wall, in the order of the geometry steps:
    /// The two side faces of the wall are looked up in it (FaceHist(3, side + 2, -1)).
    /// Null when the history is incomplete, no such step exists or its table is not counted exactly.</summary>
    internal static IReadOnlyList<WallHistoryEntry>? SelectOwnWallBaseFaceHistories(OwnWallCompleteInputs input)
    {
        var history = input.Histories.Histories;
        if (!history.Complete || history.List.ClassId is not (3524 or 361)) return null;
        foreach (var phase in history.List.Phases)
            foreach (var pointer in phase.Members)
            {
                if (pointer.ClassId != 500) continue;
                var steps = history.Steps.Where(step => step.QueueIndex == pointer.QueueIndex).ToArray();
                if (steps is not [var step] || step.ClassId != 500 || step.OwnerReference != 2) return null;
                var tables = step.Tables.Where(table => table.Name == "m_faceHistTable").ToArray();
                return tables is [var table] && table.Count == table.Entries.Count ? table.Entries : null;
            }
        return null;
    }

    /// <summary>The trimmed curve of a straight wall: the location line bounded by the extreme parameters of the
    /// vertices of its two side faces (the id of a side face is the first id of FaceHist(3, side + 2, -1) in the first BaseWallGStep,
    /// which the file keys as [side + 1, -1, -1]); every loop of a face counts. The accumulators start as in the expected export (minimum 1e30,
    /// maximum 1e-30): without a vertex the end parameter of the line stands in, and so it does for a maximum that is not above
    /// 1e-10. Null when either side face or its vertices are not found, or the range is empty: the caller keeps the full line.</summary>
    internal static (double Lo, double Hi)? SelectOwnWallTrimmedRange(OwnGElementGeometryGraph graph,
        IReadOnlyList<WallHistoryEntry> table, GRepData body, CurveDriverData axis)
    {
        double[] origin = axis.OriginPoint, direction = axis.DirectionVector;
        double lengthSquared = direction.Sum(v => v * v);
        if (axis.IsArc || axis.EndParams is not { Length: 2 } || !(lengthSquared > 0)) return null;
        double lo = 1e30, hi = 1e-30;
        for (int side = 0; side < 2; side++)
        {
            var entries = table.Where(entry => entry.Keys.SequenceEqual(new[] { side + 1, -1, -1 })).ToArray();
            if (entries is not [{ Id: >= 0 } entry] || SelectOwnWallFaceHandle(graph, entry.Id) is not uint handle) return null;
            int points = 0;
            foreach (var face in body.Faces.Where(face => face.SourceFace == handle))
                foreach (var p in (face.Vertices ?? []).Concat((face.InnerVertices ?? []).SelectMany(ring => ring)))
                {
                    double t = ((p[0] - origin[0]) * direction[0] + (p[1] - origin[1]) * direction[1]
                        + (p[2] - origin[2]) * direction[2]) / lengthSquared;
                    if (!double.IsFinite(t)) return null;
                    if (lo > t + 1e-10) lo = t;
                    if (t > hi + 1e-10) hi = t;
                    points++;
                }
            if (points == 0) return null;
        }
        if (lo + 1e-10 >= 1e30) lo = axis.EndParams[0];
        if (hi <= 1e-10) hi = axis.EndParams[1];
        return lo > hi + 1e-10 ? null : (lo, hi);
    }

    /// <summary>The queue-index handle of the one Face node whose GInfo tag is <paramref name="tag"/>; null when none or several.</summary>
    internal static uint? SelectOwnWallFaceHandle(OwnGElementGeometryGraph graph, int tag)
    {
        var tagged = graph.Nodes.Where(node => node.Fields.Any(field => field.Declaring == 1246
            && field.Name == "m_GInfo" && field.Items is [{ Values.Count: > 0 } info] && info.Values[0] == tag)).ToArray();
        return tagged is [{ ClassId: 1589 } face] ? (uint)face.QueueIndex + 1 : null;
    }
}
