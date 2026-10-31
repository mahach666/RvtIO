namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceConstraintPlane(IReadOnlyList<long> OriginBits,
        IReadOnlyList<long> XBits, IReadOnlyList<long> YBits, IReadOnlyList<long> NormalBits,
        IReadOnlyList<long> ZeroUvPointBits, IReadOnlyList<long> DistanceBits, bool EarlyGateBothVersions);

    /// <summary>Finite axis-selection and projection path only. Initial axis order Z, Y, X, a squared-cosine gate, then
    /// the next cyclic axis with sign(dot) > 0 else -1, projection and component division normalization.
    /// When all axes are rejected the fallback uses normal x (normal x axisX).
    /// Non-finite arithmetic and the warning policy are explicitly unsupported.</summary>
    internal static IReadOnlyList<long>? SelectSourceOrientedOrthogonalVector(IReadOnlyList<long> normal,
        out string? failure)
    {
        failure = "source-oriented-orthogonal";
        if (normal.Count != 3) return null;
        var n = normal.Select(BitConverter.Int64BitsToDouble).ToArray();
        if (n.Any(v => !double.IsFinite(v))) return null;
        double square = (n[0] * n[0] + n[1] * n[1]) + n[2] * n[2];
        if (!double.IsFinite(square) || 1e-18 > square) return null;
        for (int axis = 2; axis >= 0; axis--)
        {
            double[] candidate = [0, 0, 0]; candidate[axis] = 1;
            double dot = (n[0] * candidate[0] + n[1] * candidate[1]) + n[2] * candidate[2];
            double cosineSquared = (dot / square) * (dot / 1.0);
            if (!double.IsFinite(cosineSquared)) return null;
            if (!(BitConverter.Int64BitsToDouble(0x3feffff99c990193) <= cosineSquared)) continue;
            double[] output = [0, 0, 0]; output[(axis + 1) % 3] = 1;
            if (!(dot > 0)) for (int i = 0; i < 3; i++) output[i] *= -1.0;
            double projection = (n[1] * output[1] + n[0] * output[0]) + n[2] * output[2];
            if (Math.Abs(projection) > 1e-9)
            {
                double negative = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(projection / square) ^ long.MinValue);
                for (int i = 0; i < 3; i++) output[i] = negative * n[i] + output[i];
                var unit = EvaluateSourceVectorUnit(output.Select(BitConverter.DoubleToInt64Bits).ToArray(), out _);
                if (unit == null) return null;
                output = unit.UnitBits.Select(BitConverter.Int64BitsToDouble).ToArray();
            }
            if (output.Any(v => !double.IsFinite(v))) return null;
            failure = null; return output.Select(BitConverter.DoubleToInt64Bits).ToArray();
        }
        double[] axisX = [1, 0, 0];
        var cross = Cross(n, axisX); var fallback = Cross(n, cross);
        var fallbackUnit = EvaluateSourceVectorUnit(fallback.Select(BitConverter.DoubleToInt64Bits).ToArray(), out _);
        if (fallbackUnit == null) return null;
        failure = null; return fallbackUnit.UnitBits;

        static double[] Cross(double[] a, double[] b) =>
            [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    }

    /// <summary>Finite constraint preparation and the early distance gate for a null parameter pointer.
    /// Does not port the Newton solver or assume the runtime refinement flags.</summary>
    internal static SourceConstraintPlane? PrepareSourceConstraintPlane(IReadOnlyList<long> tangent,
        IReadOnlyList<long> firstPoint, IReadOnlyList<long> secondPoint, out string? failure)
    {
        failure = "source-constraint-plane";
        if (firstPoint.Count != 3 || secondPoint.Count != 3 || firstPoint.Concat(secondPoint)
            .Any(b => !double.IsFinite(BitConverter.Int64BitsToDouble(b)))) return null;
        var normal1 = EvaluateSourceVectorUnit(tangent, out _);
        if (normal1 == null || normal1.Code != 0) return null;
        var normal2 = EvaluateSourceVectorUnit(normal1.UnitBits, out _);
        if (normal2 == null || normal2.Code != 0) return null;
        var orthogonal = SelectSourceOrientedOrthogonalVector(normal2.UnitBits, out _);
        if (orthogonal == null || !Orthogonal(normal2.UnitBits, orthogonal)) return null;
        var x = EvaluateSourceVectorUnit(orthogonal, out _);
        var z = EvaluateSourceVectorUnit(normal2.UnitBits, out _);
        if (x == null || z == null || x.Code != 0 || z.Code != 0) return null;
        var a = x.UnitBits.Select(BitConverter.Int64BitsToDouble).ToArray();
        var n = z.UnitBits.Select(BitConverter.Int64BitsToDouble).ToArray();
        double[] y = [n[1] * a[2] - n[2] * a[1], n[2] * a[0] - n[0] * a[2], n[0] * a[1] - n[1] * a[0]];
        double[] origin = Enumerable.Range(0, 3).Select(i =>
            0.5 * BitConverter.Int64BitsToDouble(firstPoint[i]) + 0.5 * BitConverter.Int64BitsToDouble(secondPoint[i])).ToArray();
        double[] point = Enumerable.Range(0, 3).Select(i => (0.0 * a[i] + 0.0 * y[i]) + origin[i]).ToArray();
        if (y.Concat(origin).Concat(point).Any(v => !double.IsFinite(v))) return null;
        var pointBits = point.Select(BitConverter.DoubleToInt64Bits).ToArray();
        var d0 = EvaluateSourceVectorDistance(firstPoint, pointBits, out _);
        var d1 = EvaluateSourceVectorDistance(secondPoint, pointBits, out _);
        var d2 = EvaluateSourceVectorDistance(firstPoint, secondPoint, out _);
        if (d0 == null || d1 == null || d2 == null) return null;
        // getVertexEps for type 0 distinguishes only version <= 0 from > 0.
        // Both branches are evaluated; the actual version is not inferred.
        double legacy = BitConverter.Int64BitsToDouble(0x3f75555555555555) / 10.0;
        double positive = BitConverter.Int64BitsToDouble(0x3f7570124aff017b) / 10.0;
        long[] distances = [d0.Value, d1.Value, d2.Value];
        bool early = distances.All(b => legacy >= BitConverter.Int64BitsToDouble(b)
            && positive >= BitConverter.Int64BitsToDouble(b));
        failure = null;
        return new(origin.Select(BitConverter.DoubleToInt64Bits).ToArray(), x.UnitBits,
            y.Select(BitConverter.DoubleToInt64Bits).ToArray(), z.UnitBits, pointBits, distances, early);

        static bool Orthogonal(IReadOnlyList<long> left, IReadOnlyList<long> right)
        {
            var u = left.Select(BitConverter.Int64BitsToDouble).ToArray();
            var v = right.Select(BitConverter.Int64BitsToDouble).ToArray();
            if (u.All(c => 1e-9 > Math.Abs(c)) || v.All(c => 1e-9 > Math.Abs(c))) return true;
            double dot = (v[1] * u[1] + v[0] * u[0]) + v[2] * u[2];
            double uu = (u[1] * u[1] + u[0] * u[0]) + u[2] * u[2];
            double vv = (v[1] * v[1] + v[0] * v[0]) + v[2] * v[2];
            double angle = BitConverter.Int64BitsToDouble(0x3f5c987103b761ed);
            double bound = ((uu * angle) * vv) * angle;
            return double.IsFinite(bound) && bound > dot * dot;
        }
    }
}
