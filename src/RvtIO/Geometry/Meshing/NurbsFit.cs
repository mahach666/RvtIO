namespace RvtIO.Geometry.Meshing;

internal static class NurbsFit
{
    private const int Degree = 3;

    /// <summary>The samples with consecutive duplicates (closer than 1e-9) removed.</summary>
    internal static List<double[]> Distinct(IEnumerable<double[]> samples)
    {
        var result = new List<double[]>();
        foreach (var p in samples)
            if (result.Count == 0 || Math.Abs(p[0] - result[^1][0]) > 1e-9 || Math.Abs(p[1] - result[^1][1]) > 1e-9 || Math.Abs(p[2] - result[^1][2]) > 1e-9)
                result.Add(p);
        return result;
    }

    /// <summary>The interpolating spline of at least three distinct samples; null when the system is singular.</summary>
    internal static Nurbs? Fit(IReadOnlyList<double[]> samples) => FitValues(samples, ChordLengths(samples));

    /// <summary>The cumulative chord lengths of the samples (the knots of the interpolating spline).</summary>
    internal static double[] ChordLengths(IReadOnlyList<double[]> samples)
    {
        var chord = new double[samples.Count];
        for (int i = 1; i < samples.Count; i++)
        {
            double dx = samples[i][0] - samples[i - 1][0], dy = samples[i][1] - samples[i - 1][1], dz = samples[i][2] - samples[i - 1][2];
            chord[i] = chord[i - 1] + Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        return chord;
    }

    /// <summary>The interpolating spline of the values (any dimension) at the given parameters: the parameter curve of an edge on a face is the
    /// interpolation of its uv samples with the knots of the 3D edge curve.</summary>
    internal static Nurbs? FitValues(IReadOnlyList<double[]> samples, double[] chord)
    {
        int n = samples.Count, dim = samples[0].Length;
        if (n < 3 || chord.Length != n || !(chord[^1] > 0.0)) return null;
        var knots = new double[n + 6];
        for (int i = 0; i < 4; i++) { knots[i] = chord[0]; knots[n + 2 + i] = chord[n - 1]; }
        for (int i = 1; i < n - 1; i++) knots[3 + i] = chord[i];
        int m = n + 2;
        var a = new double[m, m + dim];
        FillDerivativeRow(knots, chord[0], 0, a, m);
        for (int r = 0; r < n; r++)
        {
            var row = Basis(knots, chord[r]);
            for (int j = 0; j < m; j++) a[r + 1, j] = row[j];
            for (int k = 0; k < dim; k++) a[r + 1, m + k] = samples[r][k];
        }
        FillDerivativeRow(knots, chord[n - 1], m - 1, a, m);
        for (int c = 0; c < m; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < m; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[pivot, c])) pivot = r;
            if (Math.Abs(a[pivot, c]) < 1e-14) return null;
            if (pivot != c) for (int j = 0; j < m + dim; j++) (a[c, j], a[pivot, j]) = (a[pivot, j], a[c, j]);
            for (int r = c + 1; r < m; r++)
            {
                double f = a[r, c] / a[c, c];
                if (f == 0.0) continue;
                for (int j = c; j < m + dim; j++) a[r, j] -= f * a[c, j];
            }
        }
        var cps = new double[m][];
        for (int i = m - 1; i >= 0; i--)
        {
            var p = new double[dim];
            for (int k = 0; k < dim; k++)
            {
                double s = a[i, m + k];
                for (int j = i + 1; j < m; j++) s -= a[i, j] * cps[j][k];
                p[k] = s / a[i, i];
            }
            cps[i] = p;
        }
        return new Nurbs { Degree = Degree, Knots = knots, ControlPoints = cps };
    }

    private static int Span(double[] knots, double t, int count)
    {
        // clamped cubic: the last non empty span for t at the end
        if (t >= knots[count]) return count - 1;
        int s = Degree;
        while (s < count - 1 && t >= knots[s + 1]) s++;
        return s;
    }

    /// <summary>All basis functions at t (only four are non zero).</summary>
    private static double[] Basis(double[] knots, double t)
    {
        int count = knots.Length - Degree - 1;
        var result = new double[count];
        int span = Span(knots, t, count);
        var n = new double[Degree + 1]; var left = new double[Degree + 1]; var right = new double[Degree + 1];
        n[0] = 1.0;
        for (int j = 1; j <= Degree; j++)
        {
            left[j] = t - knots[span + 1 - j]; right[j] = knots[span + j] - t;
            double saved = 0.0;
            for (int r = 0; r < j; r++)
            {
                double tmp = n[r] / (right[r + 1] + left[j - r]);
                n[r] = saved + right[r + 1] * tmp;
                saved = left[j - r] * tmp;
            }
            n[j] = saved;
        }
        for (int j = 0; j <= Degree; j++) result[span - Degree + j] = n[j];
        return result;
    }

    /// <summary>Row of the second derivatives of the basis functions at an end of the clamped knot vector (the NURBS book A2.3).</summary>
    private static void FillDerivativeRow(double[] knots, double t, int row, double[,] a, int m)
    {
        int count = knots.Length - Degree - 1;
        int span = Span(knots, t, count);
        const int nd = 2;
        var ndu = new double[Degree + 1, Degree + 1]; var left = new double[Degree + 1]; var right = new double[Degree + 1];
        ndu[0, 0] = 1.0;
        for (int j = 1; j <= Degree; j++)
        {
            left[j] = t - knots[span + 1 - j]; right[j] = knots[span + j] - t;
            double saved = 0.0;
            for (int r = 0; r < j; r++)
            {
                ndu[j, r] = right[r + 1] + left[j - r];
                double tmp = ndu[r, j - 1] / ndu[j, r];
                ndu[r, j] = saved + right[r + 1] * tmp;
                saved = left[j - r] * tmp;
            }
            ndu[j, j] = saved;
        }
        var ders = new double[nd + 1, Degree + 1];
        for (int j = 0; j <= Degree; j++) ders[0, j] = ndu[j, Degree];
        var rows = new double[2, Degree + 1];
        for (int r = 0; r <= Degree; r++)
        {
            int s1 = 0, s2 = 1;
            rows[0, 0] = 1.0;
            for (int k = 1; k <= nd; k++)
            {
                double d = 0.0;
                int rk = r - k, pk = Degree - k;
                if (r >= k)
                {
                    rows[s2, 0] = rows[s1, 0] / ndu[pk + 1, rk];
                    d = rows[s2, 0] * ndu[rk, pk];
                }
                int j1 = rk >= -1 ? 1 : -rk;
                int j2 = r - 1 <= pk ? k - 1 : Degree - r;
                for (int j = j1; j <= j2; j++)
                {
                    rows[s2, j] = (rows[s1, j] - rows[s1, j - 1]) / ndu[pk + 1, rk + j];
                    d += rows[s2, j] * ndu[rk + j, pk];
                }
                if (r <= pk)
                {
                    rows[s2, k] = -rows[s1, k - 1] / ndu[pk + 1, r];
                    d += rows[s2, k] * ndu[r, pk];
                }
                ders[k, r] = d;
                (s1, s2) = (s2, s1);
            }
        }
        double factor = Degree;
        for (int k = 1; k <= nd; k++)
        {
            for (int j = 0; j <= Degree; j++) ders[k, j] *= factor;
            factor *= Degree - k;
        }
        for (int j = 0; j <= Degree; j++) a[row, span - Degree + j] = ders[2, j];
    }
}
