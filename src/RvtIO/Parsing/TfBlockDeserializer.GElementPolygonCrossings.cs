namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourcePolygonCrossings(int Crossings, bool IsInside,
        IReadOnlyList<long?> IntersectionXBits);

    /// <summary>Left-ray crossing arithmetic and parity with a null optional envelope. Input is the ordered UV sequence of the
    /// iterator, not a deduplicated or reordered polygon. This kernel does not implement iterator extraction, XYZ
    /// boundary projection, chains, the outside fallback policy or shouldBeCCW sample retries.</summary>
    internal static SourcePolygonCrossings? EvaluateSourcePolygonCrossings(
        IReadOnlyList<IReadOnlyList<long>> orderedUvBits, IReadOnlyList<long> queryUvBits,
        out string? failure)
    {
        failure = "polygon-crossings";
        if (orderedUvBits.Count == 0 || queryUvBits.Count != 2) return null;
        double qx = BitConverter.Int64BitsToDouble(queryUvBits[0]);
        double qy = BitConverter.Int64BitsToDouble(queryUvBits[1]);
        if (!double.IsFinite(qx) || !double.IsFinite(qy)) return null;
        var points = new (double X, double Y)[orderedUvBits.Count];
        for (int i = 0; i < points.Length; i++)
        {
            if (orderedUvBits[i].Count != 2) return null;
            double x = BitConverter.Int64BitsToDouble(orderedUvBits[i][0]);
            double y = BitConverter.Int64BitsToDouble(orderedUvBits[i][1]);
            if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
            points[i] = (x, y);
        }
        var intersections = new List<long?>(points.Length);
        int crossings = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[i + 1 == points.Length ? 0 : i + 1];
            // The comparison sides are deliberately asymmetric: qY/bY must equal aY/qY.
            // No EPS or standard ray formula is substituted.
            if ((qy > b.Y) != (a.Y >= qy)) { intersections.Add(null); continue; }
            double intersection = ((a.X - b.X) * (qy - a.Y)) / (a.Y - b.Y) + a.X;
            if (!double.IsFinite(intersection)) return null;
            intersections.Add(BitConverter.DoubleToInt64Bits(intersection));
            if (qx > intersection) crossings++;
        }
        failure = null;
        return new(crossings, (crossings & 1) != 0, intersections);
    }
}
