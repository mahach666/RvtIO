namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    internal static double? ComputeOwnProfileRoofArea(TfBlockDeserializer.OwnGElementGeometryGraph graph,
        TfBlockDeserializer.ProfileRoofAreaFaceTags tags)
    {
        if (graph.OwnerId != tags.OwnerId || tags.Top.Length == 0 || tags.Bottom.Length == 0
            || graph.Nodes.Any(n => n.ClassId is 1991 or 2238 or 2015 or 1996 or 1999)
            || graph.Nodes.Count(n => n.ClassId == 2092) != 1) return null;
        var body = graph.Nodes.Single(n => n.ClassId == 2092);
        if (!graph.Nodes[0].Fields.Single(f => f.Declaring == 2009 && f.Name == "m_subNodes")
                .Items.Any(item => item.Pointer?.QueueIndex == body.QueueIndex)
            || graph.Nodes.Where(n => n.ClassId == 1589).Any(n => n.Fields
                .Single(f => f.Declaring == 1590 && f.Name == "m_faceRegions").Items.Count != 0)) return null;
        // Forced analytic curves need their native integral rather than a polygon chord.
        if (graph.Nodes.Where(n => n.ClassId == 1243).Any(n => (n.Fields
                .Single(f => f.Declaring == 1244 && f.Name == "m_flags").Items.Single().Values[0] & 8) != 0)) return null;
        var regions = SelectOwnPlanarFaceRegions(graph, out _);
        if (regions == null) return null;
        var faces = graph.Nodes.Where(n => n.ClassId == 1589).ToArray();
        var tagged = faces.GroupBy(n => n.Fields.Single(f => f.Declaring == 1246 && f.Name == "m_GInfo")
            .Items.Single().Values[0]).ToDictionary(g => g.Key, g => g.ToArray());
        double result = 0;
        foreach (var side in new[] { tags.Top, tags.Bottom })
        {
            double area = 0;
            foreach (int tag in side)
            {
                if (!tagged.TryGetValue(tag, out var own) || own.Length != 1) return null;
                // A face without loops integrates to zero, including a consumed top face.
                foreach (var region in regions.GetValueOrDefault(own[0].QueueIndex) ?? [])
                {
                    double net = NewellNormalArea(region.Vertices!).Area
                        - (region.InnerVertices ?? []).Sum(ring => NewellNormalArea(ring).Area);
                    if (!double.IsFinite(net) || net <= 0) return null;
                    area += net;
                }
            }
            if (area > result + 1e-10) result = area;
        }
        return result > 0 && double.IsFinite(result) ? result : null;
    }
}
