namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceWindingDensity(int Code, long DenominatorBits, IReadOnlyList<long>? GradientBits);

    /// <summary>Squared UV radius (Y+X order) times the exact tau bits 0x401921FB54442D11 (not Math.Tau).
    /// A singular |denominator| at or below 1e-9 returns -1. Otherwise (-dy/den, dx/den), flipped when the face/surface
    /// orientation predicate is false. Finite admission only.</summary>
    internal static SourceWindingDensity? EvaluateSourceWindingDensity(IReadOnlyList<long> queryBits,
        IReadOnlyList<long> pointBits, bool orientation, out string? failure)
    {
        failure = "source-winding-density";
        if (queryBits.Count != 2 || pointBits.Count != 2) return null;
        double dx = BitConverter.Int64BitsToDouble(pointBits[0]) - BitConverter.Int64BitsToDouble(queryBits[0]);
        double dy = BitConverter.Int64BitsToDouble(pointBits[1]) - BitConverter.Int64BitsToDouble(queryBits[1]);
        if (queryBits.Concat(pointBits).Any(b => !double.IsFinite(BitConverter.Int64BitsToDouble(b)))
            || !double.IsFinite(dx) || !double.IsFinite(dy)) return null;
        double denominator = (dy * dy + dx * dx) * BitConverter.Int64BitsToDouble(0x401921fb54442d11);
        if (!double.IsFinite(denominator)) return null;
        long bits = BitConverter.DoubleToInt64Bits(denominator);
        if (BitConverter.Int64BitsToDouble(0x3e112e0be826d695) >= Math.Abs(denominator))
        { failure = null; return new(-1, bits, null); }
        long x = BitConverter.DoubleToInt64Bits(dy / denominator) ^ long.MinValue;
        long y = BitConverter.DoubleToInt64Bits(dx / denominator);
        if (!orientation) { x ^= long.MinValue; y ^= long.MinValue; }
        failure = null; return new(0, bits, [x, y]);
    }
}
