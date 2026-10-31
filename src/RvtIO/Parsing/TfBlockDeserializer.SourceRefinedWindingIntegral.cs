namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceWindingPlane(IReadOnlyList<long> OriginBits, IReadOnlyList<long> XBits, IReadOnlyList<long> YBits);
    internal sealed record SourceRefinedWindingInput(IReadOnlyList<IReadOnlyList<long>> FirstBySideBits,
        IReadOnlyList<IReadOnlyList<long>> LastBySideBits, IReadOnlyList<SourceWindingPlane> Planes,
        int FaceSide, int EdgeFlags, IReadOnlyList<long> QueryBits, bool Orientation);
    internal sealed record SourceRefinedWindingIntegral(SourceScalarIntegral Integral,
        int ConstraintPreparations, int EarlyGatePasses, bool EveryVisitedRefinedPointResolved);

    /// <summary>Refined callback for tessellated or null-curve paths: raw evalUV(t), dynamic constraint preparation and
    /// early gate, the two-point derivative and a separate face/flag sign. No ordinary parameter reversal on the
    /// refined path. Newton, non-finite and error-fallback paths are unsupported, not guessed; both EPS version
    /// branches must resolve identically.</summary>
    internal static SourceRefinedWindingIntegral? IntegrateSourceRefinedWindingSegment(
        SourceRefinedWindingInput input, double epsilon, out string? failure)
    {
        failure = "source-refined-winding-input";
        if (input.FaceSide is < 0 or > 1 || input.EdgeFlags is < 0 or > 255 || (input.EdgeFlags & 6) != 6 || input.Planes.Count != 2
            || input.FirstBySideBits.Count != 2 || input.LastBySideBits.Count != 2 || input.QueryBits.Count != 2) return null;
        var all = input.FirstBySideBits.Concat(input.LastBySideBits).ToArray();
        if (all.Any(v => v.Count != 2) || input.Planes.Any(p => p.OriginBits.Count != 3 || p.XBits.Count != 3 || p.YBits.Count != 3)) return null;
        if (all.SelectMany(v => v).Concat(input.QueryBits).Concat(input.Planes.SelectMany(p => p.OriginBits.Concat(p.XBits).Concat(p.YBits)))
            .Any(b => !double.IsFinite(BitConverter.Int64BitsToDouble(b)))) return null;
        double[][] first = input.FirstBySideBits.Select(v => v.Select(BitConverter.Int64BitsToDouble).ToArray()).ToArray();
        double[][] last = input.LastBySideBits.Select(v => v.Select(BitConverter.Int64BitsToDouble).ToArray()).ToArray();
        double[][] directions = Enumerable.Range(0, 2).Select(s => new[] { last[s][0] - first[s][0], last[s][1] - first[s][1] }).ToArray();
        if (directions.SelectMany(v => v).Any(v => !double.IsFinite(v))) return null;
        bool reverse = (input.FaceSide == 1) != ((input.EdgeFlags & 1) != 0);
        var derivative = directions[input.FaceSide].ToArray();
        if (reverse) for (int i = 0; i < 2; i++) derivative[i] = Negate(derivative[i]);
        int preparations = 0, gates = 0; string? callbackFailure = null;
        var integral = IntegrateSourceScalar(parameter =>
        {
            double[][] uv = Enumerable.Range(0, 2).Select(s => new[] {
                (1.0 - parameter) * first[s][0] + parameter * last[s][0],
                (1.0 - parameter) * first[s][1] + parameter * last[s][1] }).ToArray();
            double[][] xyz = new double[2][];
            for (int side = 0; side < 2; side++)
            {
                var p = input.Planes[side];
                xyz[side] = Enumerable.Range(0, 3).Select(i =>
                    (uv[side][0] * Number(p.XBits[i]) + uv[side][1] * Number(p.YBits[i])) + Number(p.OriginBits[i])).ToArray();
            }
            var plane0 = input.Planes[0];
            var tangent = Enumerable.Range(0, 3).Select(i => BitConverter.DoubleToInt64Bits(
                Number(plane0.XBits[i]) * directions[0][0] + Number(plane0.YBits[i]) * directions[0][1])).ToArray();
            preparations++;
            var prepared = PrepareSourceConstraintPlane(tangent, xyz[0].Select(BitConverter.DoubleToInt64Bits).ToArray(),
                xyz[1].Select(BitConverter.DoubleToInt64Bits).ToArray(), out _);
            if (prepared == null) { callbackFailure = "source-refined-constraint"; return (-1, 0); }
            if (!prepared.EarlyGateBothVersions) { callbackFailure = "source-refined-intersection-pending"; return (-1, 0); }
            gates++;
            var density = EvaluateSourceWindingDensity(input.QueryBits,
                uv[input.FaceSide].Select(BitConverter.DoubleToInt64Bits).ToArray(), input.Orientation, out callbackFailure);
            if (density == null) return (-1, 0);
            if (density.Code != 0) return (density.Code, 0);
            var gradient = density.GradientBits!;
            double value = (Number(gradient[1]) * derivative[1] + Number(gradient[0]) * derivative[0]) + 0.0;
            return (0, BitConverter.DoubleToInt64Bits(value));
        }, 0, 1, epsilon, out failure);
        if (callbackFailure != null) { failure = callbackFailure; return null; }
        if (integral == null) return null;
        return new(integral, preparations, gates, preparations == gates);

        static double Number(long bits) => BitConverter.Int64BitsToDouble(bits);
        static double Negate(double value) => Number(BitConverter.DoubleToInt64Bits(value) ^ long.MinValue);
    }
}
