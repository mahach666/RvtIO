namespace RvtIO.Geometry.Meshing;

internal static class FaceArea
{
    private const double Tolerance = 1e-6;
    private const int MaxLevels = 20;

    /// <summary>One coedge of a loop: the points of its Edge as stored for this Face, the Edge flags, and whether this is the second
    /// Face of the Edge.</summary>
    internal readonly record struct Coedge(IReadOnlyList<(double U, double V)> Points, int Flags, bool SecondFace);

    /// <summary>
    /// The area of the Face whose loops are the coedges, in the square of the unit of its (u, v); null when the quadrature does
    /// not converge or a coedge has fewer than two points.
    /// </summary>
    internal static double? Compute(IEnumerable<Coedge> coedges)
    {
        double total = 0;
        foreach (var coedge in coedges)
        {
            if (coedge.Points.Count < 2 || Integrate(coedge) is not { } value) return null;
            total += value;
        }
        return Math.Abs(total);
    }

    internal static (double[] Value, double[] Derivative) HermiteBasis(double s)
    {
        double twoSquare = (s + s) * s, threeSquare = s * 3.0 * s, cube = s * s * s, sixSquare = s * 6.0 * s;
        return ([twoSquare * s - threeSquare + 1.0, threeSquare - twoSquare * s, cube - twoSquare + s, cube - s * s],
            [sixSquare - s * 6.0, s * 6.0 - sixSquare, threeSquare - s * 4.0 + 1.0, threeSquare - (s + s)]);
    }

    private static (double U, double V)[] Coefficients(IReadOnlyList<(double U, double V)> points, int segment)
    {
        int interior = points.Count - 2;
        var a = points[segment];
        var b = points[segment + 1];
        (double U, double V) tangentA, tangentB;
        if (interior == 0)
        {
            tangentA = tangentB = (b.U - a.U, b.V - a.V);
        }
        else
        {
            if (segment == interior)
            {
                var before = points[segment - 1];
                tangentB = ((3.0 * b.U - 4.0 * a.U + before.U) * 0.5, (3.0 * b.V - 4.0 * a.V + before.V) * 0.5);
            }
            else
            {
                var after = points[segment + 2];
                tangentB = ((after.U - a.U) * 0.5, (after.V - a.V) * 0.5);
            }
            if (segment != 0)
            {
                var before = points[segment - 1];
                tangentA = ((b.U - before.U) * 0.5, (b.V - before.V) * 0.5);
            }
            else
            {
                var after = points[2];
                tangentA = ((b.U * 4.0 - a.U * 3.0 - after.U) * 0.5, (b.V * 4.0 - a.V * 3.0 - after.V) * 0.5);
            }
        }
        return [a, b, tangentA, tangentB];
    }

    private static ((double U, double V) Point, (double U, double V) Derivative) Evaluate(IReadOnlyList<(double U, double V)> points, double t)
    {
        int count = points.Count;
        if (count == 2)
        {
            var (a, b) = (points[0], points[1]);
            return ((a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t), (b.U - a.U, b.V - a.V));
        }
        double intervals = count - 1;
        int segment = Math.Min((int)Math.Floor(intervals * t), count - 2);
        var (basis, derivative) = HermiteBasis(intervals * t - segment);
        var c = Coefficients(points, segment);
        double u = basis[0] * c[0].U, v = basis[0] * c[0].V, du = derivative[0] * c[0].U, dv = derivative[0] * c[0].V;
        for (int k = 1; k < 4; k++)
        {
            u = basis[k] * c[k].U + u;
            v = basis[k] * c[k].V + v;
            du = derivative[k] * c[k].U + du;
            dv = derivative[k] * c[k].V + dv;
        }
        return ((u, v), (du * intervals, dv * intervals));
    }

    private static double Integrand(Coedge coedge, double t)
    {
        bool reverse = (coedge.Flags & 1) != (coedge.SecondFace ? 1 : 0);
        var (point, derivative) = Evaluate(coedge.Points, reverse ? 1.0 - t : t);
        if (reverse) derivative = (-derivative.U, -derivative.V);
        return 0.5 * (point.U * derivative.V - point.V * derivative.U);
    }

    private static double? Integrate(Coedge coedge)
    {
        double previous = (Integrand(coedge, 0.0) + Integrand(coedge, 1.0)) * 0.5;
        var step = new List<double> { 1.0 };
        var estimate = new List<double> { previous };
        int level = 1;
        for (; level <= 4; level++)
        {
            previous = Refine(coedge, level, estimate[^1]);
            step.Add(step[^1] * 0.25);
            estimate.Add(previous);
        }
        var (value, error) = Extrapolate(step, estimate);
        if (Math.Abs(value) <= Tolerance) return value;
        while (Math.Abs(error) > Math.Abs(value) * Tolerance)
        {
            if (level >= MaxLevels) return null;
            step.Add(step[^1] * 0.25);
            estimate.Add(Refine(coedge, level, estimate[^1]));
            step.RemoveAt(0);
            estimate.RemoveAt(0);
            (value, error) = Extrapolate(step, estimate);
            level++;
            if (Math.Abs(value) <= Tolerance) break;
        }
        return value;
    }

    private static double Refine(Coedge coedge, int level, double previous)
    {
        int panels = 1 << (level - 1);
        double width = 1.0 / panels, t = width * 0.5 + 0.0, sum = 0;
        for (int i = 0; i < panels; i++)
        {
            sum += Integrand(coedge, t);
            t += width;
        }
        return (sum * 1.0 / panels + previous) * 0.5;
    }

    private static (double Value, double Error) Extrapolate(List<double> step, List<double> estimate)
    {
        int n = step.Count;
        var c = estimate.ToArray();
        var d = estimate.ToArray();
        double value = estimate[n - 1], error = 0;
        for (int m = 1; m < n; m++)
        {
            for (int i = 0; i < n - m; i++)
            {
                double den = (c[i + 1] - d[i]) / (step[i] - step[i + m]);
                d[i] = step[i + m] * den;
                c[i] = step[i] * den;
            }
            error = d[n - m - 1];
            value += error;
        }
        return (value, error);
    }
}
