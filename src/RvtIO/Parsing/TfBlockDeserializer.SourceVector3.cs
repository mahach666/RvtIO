namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceVectorUnit(int Code, long LengthBits, IReadOnlyList<long> UnitBits);

    /// <summary>Finite path: (x*x + y*y) + z*z then sqrt.
    /// No hypot rescaling. The non-finite warning policy is unsupported.</summary>
    internal static long? EvaluateSourceVectorLength(IReadOnlyList<long> vector, out string? failure)
    {
        failure = "source-vector-length";
        if (vector.Count != 3) return null;
        var v = vector.Select(BitConverter.Int64BitsToDouble).ToArray();
        if (v.Any(x => !double.IsFinite(x))) return null;
        double square = (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2];
        if (!double.IsFinite(square)) return null;
        failure = null; return BitConverter.DoubleToInt64Bits(Math.Sqrt(square));
    }

    /// <summary>Length strictly below 1e-9 returns code 4 with a positive-zero output; otherwise divides each
    /// component. Equality is accepted. The non-finite path is not a fabricated code.</summary>
    internal static SourceVectorUnit? EvaluateSourceVectorUnit(IReadOnlyList<long> vector, out string? failure)
    {
        var lengthBits = EvaluateSourceVectorLength(vector, out failure); if (lengthBits == null) return null;
        double length = BitConverter.Int64BitsToDouble(lengthBits.Value);
        if (1e-9 > length) return new(4, lengthBits.Value, [0, 0, 0]);
        return new(0, lengthBits.Value, vector.Select(b =>
            BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(b) / length)).ToArray());
    }

    /// <summary>Subtracts left - right in X, Y, Z order and delegates to the same unscaled finite length path.</summary>
    internal static long? EvaluateSourceVectorDistance(IReadOnlyList<long> left, IReadOnlyList<long> right,
        out string? failure)
    {
        failure = "source-vector-distance";
        if (left.Count != 3 || right.Count != 3) return null;
        return EvaluateSourceVectorLength(Enumerable.Range(0, 3).Select(i => BitConverter.DoubleToInt64Bits(
            BitConverter.Int64BitsToDouble(left[i]) - BitConverter.Int64BitsToDouble(right[i]))).ToArray(), out failure);
    }
}
