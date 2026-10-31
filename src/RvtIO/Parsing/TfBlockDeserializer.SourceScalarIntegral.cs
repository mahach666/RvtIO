namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal delegate (int Code, long ValueBits) SourceScalarIntegrand(double parameter);
    internal sealed record SourceScalarIntegral(int Code, long? ValueBits, long? ErrorBits, int Levels, int Evaluations);

    /// <summary>Finite one-dimensional path: 20 trapezoid levels, five-point polynomial extrapolation to 0, h *= 0.25.
    /// The |result| at or below 1e-6 shortcut accepts without a relative-error test. Callback error codes propagate;
    /// exhaustion returns -1, not a successful estimate.</summary>
    internal static SourceScalarIntegral? IntegrateSourceScalar(SourceScalarIntegrand? integrand,
        double begin, double end, double epsilon, out string? failure)
    {
        failure = "source-scalar-integral";
        if (integrand == null || !double.IsFinite(begin) || !double.IsFinite(end) || !double.IsFinite(epsilon)) return null;
        if (epsilon <= 0) { failure = null; return new(-1, null, null, 0, 0); }
        double width = end - begin; if (!double.IsFinite(width)) return null;
        var values = new double[20]; var h = new double[20]; h[0] = 1;
        int evaluations = 0; double previous = 0; long? resultBits = null, errorBits = null;
        for (int level = 0; level < 20; level++)
        {
            if (level == 0)
            {
                var first = Evaluate(begin); if (!first.Supported) return null; if (first.Code != 0) { failure = null; return Error(first.Code, level); }
                var last = Evaluate(end); if (!last.Supported) return null; if (last.Code != 0) { failure = null; return Error(last.Code, level); }
                previous = (last.Value + first.Value) * (width * 0.5);
            }
            else
            {
                int count = 1 << (level - 1); double step = width / count;
                double parameter = step * 0.5 + begin; double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    var sample = Evaluate(parameter); if (!sample.Supported) return null; if (sample.Code != 0) { failure = null; return Error(sample.Code, level); }
                    sum = sample.Value + sum; parameter += step;
                }
                previous = ((width * sum) / count + previous) * 0.5;
            }
            if (!double.IsFinite(previous)) return null;
            values[level] = previous;
            if (level != 0) h[level] = h[level - 1] * 0.25;
            if (level < 4) continue;
            var estimate = Extrapolate(h.AsSpan(level - 4, 5), values.AsSpan(level - 4, 5));
            if (estimate == null) return null;
            resultBits = BitConverter.DoubleToInt64Bits(estimate.Value.Value);
            errorBits = BitConverter.DoubleToInt64Bits(estimate.Value.Error);
            if (!(Math.Abs(estimate.Value.Value) > 1e-6 && Math.Abs(estimate.Value.Error) > Math.Abs(estimate.Value.Value) * epsilon))
            { failure = null; return new(0, resultBits, errorBits, level + 1, evaluations); }
        }
        failure = null; return new(-1, resultBits, errorBits, 20, evaluations);

        (int Code, double Value, bool Supported) Evaluate(double parameter)
        {
            evaluations++; var sample = integrand(parameter); double value = BitConverter.Int64BitsToDouble(sample.ValueBits);
            // Unsupported non-finite callback arithmetic is not an error-code success.
            return sample.Code != 0 ? (sample.Code, 0, true) : (0, value, double.IsFinite(value));
        }
        SourceScalarIntegral Error(int code, int level) => new(code, null, null, level + 1, evaluations);
    }

    // One dimension, x = 0. h is strictly decreasing and finite;
    // the closest index picks the last point. The scalar C/D operation order is kept.
    static (double Value, double Error)? Extrapolate(ReadOnlySpan<double> x, ReadOnlySpan<double> values)
    {
        var c = values.ToArray(); var d = values.ToArray(); int nearest = 0;
        for (int i = 1; i < 5; i++) if (Math.Abs(x[i]) < Math.Abs(x[nearest])) nearest = i;
        double result = values[nearest]; nearest--; double error = 0;
        for (int order = 1; order < 5; order++)
        {
            for (int i = 0; i < 5 - order; i++)
            {
                double lower = x[i] - 0.0, upper = x[i + order] - 0.0;
                double difference = (c[i + 1] - d[i]) / (lower - upper);
                d[i] = difference * upper; c[i] = difference * lower;
                if (!double.IsFinite(c[i]) || !double.IsFinite(d[i])) return null;
            }
            error = 2 * (nearest + 1) < 5 - order ? c[nearest + 1] : d[nearest--];
            result = error + result;
        }
        return double.IsFinite(result) ? (result, error) : null;
    }
}
