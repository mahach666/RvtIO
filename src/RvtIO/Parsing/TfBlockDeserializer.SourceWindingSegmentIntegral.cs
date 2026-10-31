namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    /// <summary>Numerical UV two-point/tessellated segment dependency of the path integral, not its refined-curve,
    /// iterator or runtime decision. One evalUVAlongFaceLoop side/flag reversal, a constant signed derivative, the
    /// density, then vecDot in Y+X+Z order with third entries zero.</summary>
    internal static SourceScalarIntegral? IntegrateSourceWindingSegment(IReadOnlyList<long> first,
        IReadOnlyList<long> last, IReadOnlyList<long> query, bool orientation, bool reverse,
        double epsilon, out string? failure)
    {
        failure = "source-winding-segment";
        if (first.Count != 2 || last.Count != 2 || query.Count != 2 || first.Concat(last).Concat(query)
            .Any(b => !double.IsFinite(BitConverter.Int64BitsToDouble(b)))) return null;
        var a = first.Select(BitConverter.Int64BitsToDouble).ToArray(); var b = last.Select(BitConverter.Int64BitsToDouble).ToArray();
        var direction = new double[] { b[0] - a[0], b[1] - a[1] };
        if (direction.Any(v => !double.IsFinite(v))) return null;
        if (reverse) for (int i = 0; i < 2; i++) direction[i] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(direction[i]) ^ long.MinValue);
        string? callbackFailure = null;
        var result = IntegrateSourceScalar(parameter =>
        {
            double t = reverse ? 1.0 - parameter : parameter;
            long[] uv = [BitConverter.DoubleToInt64Bits((1.0 - t) * a[0] + t * b[0]),
                BitConverter.DoubleToInt64Bits((1.0 - t) * a[1] + t * b[1])];
            var density = EvaluateSourceWindingDensity(query, uv, orientation, out callbackFailure);
            if (density == null) return (-1, 0);
            if (density.Code != 0) return (density.Code, 0);
            var gradient = density.GradientBits!;
            double value = (BitConverter.Int64BitsToDouble(gradient[1]) * direction[1]
                + BitConverter.Int64BitsToDouble(gradient[0]) * direction[0]) + 0.0;
            return (0, BitConverter.DoubleToInt64Bits(value));
        }, 0, 1, epsilon, out failure);
        if (callbackFailure != null) { failure = callbackFailure; return null; }
        return result;
    }
}
