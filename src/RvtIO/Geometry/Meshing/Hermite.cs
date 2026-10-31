namespace RvtIO.Geometry.Meshing;

internal static class Hermite
{
    private static double[] Knots(double[] t)
    {
        var k = new List<double>();
        for (int i = 0; i < t.Length; i++)
            for (int m = 0; m < (i == 0 || i == t.Length - 1 ? 4 : 2); m++) k.Add(t[i]);
        return k.ToArray();
    }

    private static double[][] Control(double[] t, double[][] p, double[][] d)
    {
        var c = new double[2 * t.Length][];
        for (int k = 0; k < t.Length; k++)
        {
            double a = k > 0 ? t[k] - t[k - 1] : 0.0, b = k + 1 < t.Length ? t[k + 1] - t[k] : 0.0;
            c[2 * k] = p[k].Zip(d[k], (x, y) => x - a / 3.0 * y).ToArray();
            c[2 * k + 1] = p[k].Zip(d[k], (x, y) => x + b / 3.0 * y).ToArray();
        }
        return c;
    }

    private static bool Valid(double[] t) => t.Length >= 2 && t.All(double.IsFinite) && Enumerable.Range(1, t.Length - 1).All(i => t[i] > t[i - 1]);

    /// <summary>The curve through the nodes (t[k], p[k]) with the derivatives d[k]; null for fewer than two nodes or parameters that are
    /// not increasing.</summary>
    internal static Nurbs? Curve(double[] t, double[][] p, double[][] d)
    {
        if (!Valid(t) || p.Length != t.Length || d.Length != t.Length) return null;
        return new Nurbs { Degree = 3, Knots = Knots(t), ControlPoints = Control(t, p, d) };
    }

    /// <summary>The surface through the node grid; the node of (u[iu], v[iv]) is at index iv * u.Length + iu (the order of the Revit
    /// record). null for a grid smaller than 2 x 2 or parameters that are not increasing.</summary>
    internal static NurbsSurface? Surface(double[] u, double[] v, double[][] p, double[][] du, double[][] dv, double[][] duv)
    {
        int nu = u.Length, nv = v.Length, n = nu * nv;
        if (!Valid(u) || !Valid(v) || p.Length != n || du.Length != n || dv.Length != n || duv.Length != n) return null;
        // u direction for every v node: the position field with du, and the dv field with duv
        var cp = new double[2 * nu][][]; // [iu'][iv] position field
        var cd = new double[2 * nu][][]; // [iu'][iv] dv field
        for (int i = 0; i < 2 * nu; i++) { cp[i] = new double[nv][]; cd[i] = new double[nv][]; }
        for (int j = 0; j < nv; j++)
        {
            var row = Enumerable.Range(0, nu).Select(i => j * nu + i).ToArray();
            var a = Control(u, row.Select(i => p[i]).ToArray(), row.Select(i => du[i]).ToArray());
            var b = Control(u, row.Select(i => dv[i]).ToArray(), row.Select(i => duv[i]).ToArray());
            for (int i = 0; i < 2 * nu; i++) { cp[i][j] = a[i]; cd[i][j] = b[i]; }
        }
        var cps = new List<double[]>(4 * n);
        for (int i = 0; i < 2 * nu; i++) cps.AddRange(Control(v, cp[i], cd[i]));
        return new NurbsSurface { DegreeU = 3, DegreeV = 3, KnotsU = Knots(u), KnotsV = Knots(v), ControlPoints = cps.ToArray() };
    }
}
