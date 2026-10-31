namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal static int? SelectOwnWallSide0Id(OwnWallCompleteInputs input)
    {
        var history = input.Histories.Histories;
        if (!history.Complete || history.List.ClassId != 3524 || history.List.Phases.Count < 2
            || history.List.Phases[1].Members.Count == 0) return null;
        var pointer = history.List.Phases[1].Members[0];
        var steps = history.Steps.Where(step => step.QueueIndex == pointer.QueueIndex).ToArray();
        if (steps is not [var step] || step.ClassId != pointer.ClassId || step.OwnerReference != 2) return null;
        var tables = step.Tables.Where(table => table.Name == "m_faceHistTable").ToArray();
        if (tables is not [var table] || table.Count != table.Entries.Count) return null;
        var entries = table.Entries.Where(entry => entry.Keys.SequenceEqual(new[] { 1, -1, -1 })).ToArray();
        return entries is [{ Id: >= 0 } entry] ? entry.Id : null;
    }

    internal static (uint SourceFace, double[] Normal)? SelectOwnWallSide0Normal(
        OwnGElementGeometryGraph graph, int tag)
    {
        var tagged = graph.Nodes.Where(node => node.Fields.Any(field => field.Declaring == 1246
            && field.Name == "m_GInfo" && field.Items is [{ Values.Count: > 0 } info]
            && info.Values[0] == tag)).ToArray();
        if (tagged is not [{ ClassId: 1589 } face]) return null;
        var local = SelectOwnWallLocalGeometryInputs(graph, out _);
        if (local?.Geometries is not [var body] || local.FirstDirectGeometryQueueIndex != body.QueueIndex
            || !body.Faces.Any(item => item.FaceQueueIndex == face.QueueIndex && item.RegionOfQueueIndex == null))
            return null;
        var links = graph.FaceSurfaces.Where(link => link.FaceQueueIndex == face.QueueIndex).ToArray();
        if (links is not [var link]) return null;
        var planes = graph.Nodes.Where(node => node.QueueIndex == link.SurfaceQueueIndex).ToArray();
        if (planes is not [{ ClassId: 522 } plane]) return null;
        double[]? Vector(string name)
        {
            var fields = plane.Fields.Where(field => field.Declaring == 522 && field.Name == name).ToArray();
            if (fields is not [{ Items.Count: 3 } field] || field.Items.Any(item => item.Values.Count != 1)) return null;
            var values = field.Items.Select(item => BitConverter.Int64BitsToDouble(item.Values[0])).ToArray();
            return values.All(double.IsFinite) ? values : null;
        }
        if (Vector("m_xVec") is not { } x || Vector("m_yVec") is not { } y) return null;
        double[] normal = [x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0]];
        double length = Math.Sqrt(normal.Sum(value => value * value));
        if (!(length > 0) || !double.IsFinite(length)) return null;
        return ((uint)face.QueueIndex + 1, normal.Select(value => value / length).ToArray());
    }
}
