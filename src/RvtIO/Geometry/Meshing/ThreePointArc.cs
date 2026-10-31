namespace RvtIO.Geometry.Meshing;

internal static class ThreePointArc
{
    private const double Tol = 1e-10;
    private const double TwoPi = 6.283185307179586;

    /// <summary>The arc through the three points, Start 0 and End the sweep; null when the points are (nearly) collinear.</summary>
    internal static Arc? Create(double[] p1, double[] p2, double[] p3)
    {
        double v9 = p2[2] - p1[2], v10 = p2[1] - p1[1], v11 = p2[0] - p1[0];
        double v12 = p3[2] - p2[2], v13 = p3[1] - p2[1], v14 = p3[0] - p2[0];
        double[] n = { v12 * v10 - v13 * v9, v14 * v9 - v12 * v11, v13 * v11 - v14 * v10 };
        double ln = Length(n);
        if (!(Tol < ln)) return null;
        n = new[] { n[0] / ln, n[1] / ln, n[2] / ln };
        var m = PlaneToWorld(n);
        var inv = Inverse(m);
        var q1 = Apply(inv, p1); var q2 = Apply(inv, p2); var q3 = Apply(inv, p3);
        if (!Circle2d(q1, q2, q3, out var circle)) return null;
        double z0 = q1[2];
        // the circle's frame: centre (cx, cy, 0 + z0) before the plane frame is applied
        double[] center = { circle.Cx + 0.0 * z0, circle.Cy + 0.0 * z0, z0 + 0.0 };
        double r = circle.Radius;
        center = TransformPoint(center, m);
        var reference = TransformVector(new[] { circle.RefX * r, circle.RefY * r, 0.0 * r }, m);
        var perp = TransformVector(new[] { circle.PerpX * r, circle.PerpY * r, 0.0 * r }, m);
        NormalizeGetLength(reference);
        double radius = NormalizeGetLength(perp);
        return new Arc
        {
            Center = center, Radius = radius, Normal = Cross(reference, perp), Ref = reference, Start = 0.0, End = circle.Sweep,
        };
    }

    private readonly record struct Circle2(double Cx, double Cy, double RefX, double RefY, double PerpX, double PerpY, double Radius, double Sweep);

    private static double Length(double[] v) => Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);

    private static double[] Normalize(double[] v)
    {
        double r = 1.0 / Length(v);
        return new[] { v[0] * r, v[1] * r, v[2] * r };
    }

    private static double NormalizeGetLength(double[] v)
    {
        double l = Length(v);
        if (l <= 1e-300) return 0.0;
        double r = 1.0 / l;
        v[0] *= r; v[1] *= r; v[2] = r * v[2];
        return l;
    }

    private static double[] Cross(double[] a, double[] b) =>
        new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

    private static double[] PlaneToWorld(double[] normal)
    {
        var nn = Normalize(normal);
        double nx = nn[0], ny = nn[1], nz = nn[2];
        double v5, v6, v7, v8, v9, v10;
        if (Math.Abs(nx) >= 0.015625 || Math.Abs(ny) >= 0.015625)
        {
            v5 = ny * 1.0; v6 = nz * 0.0; v7 = nx * 1.0; v8 = nz * 0.0; v9 = ny * 0.0; v10 = nx * 0.0;
        }
        else
        {
            v5 = ny * 0.0; v6 = nz * 1.0; v7 = nx * 0.0; v8 = nz * 0.0; v9 = ny * 0.0; v10 = nx * 1.0;
        }
        var t = Normalize(new[] { v6 - v5, v7 - v8, v9 - v10 });
        double tx = t[0], ty = t[1], tz = t[2];
        var m = new double[16];
        m[0] = tx; m[4] = ty; m[8] = tz;
        m[1] = tz * ny - nz * ty; m[5] = nz * tx - tz * nx; m[9] = ty * nx - tx * ny;
        m[2] = nx; m[6] = ny; m[10] = nz;
        m[15] = 1.0;
        return m;
    }

    private static double[] Inverse(double[] m)
    {
        double m00 = m[0], m01 = m[1], m02 = m[2], m03 = m[3];
        double m10 = m[4], m11 = m[5], m12 = m[6], m13 = m[7];
        double m20 = m[8], m21 = m[9], m22 = m[10], m23 = m[11];
        double det = m00 * m11 * m22 + m10 * m21 * m02 + m01 * m20 * m12 - m20 * m11 * m02 - m01 * m10 * m22 - m00 * m21 * m12;
        var a = new double[16];
        a[0] = (m22 * m11 - m12 * m21) / det;
        a[1] = -(m01 * m22 - m02 * m21) / det;
        a[2] = (m01 * m12 - m11 * m02) / det;
        a[3] = -(m12 * m01 * m23 + m13 * m02 * m21 + m03 * m11 * m22 - m03 * m12 * m21 - m11 * m02 * m23 - m13 * m01 * m22) / det;
        a[4] = -(m22 * m10 - m20 * m12) / det;
        a[5] = (m22 * m00 - m02 * m20) / det;
        a[6] = -(m12 * m00 - m02 * m10) / det;
        a[7] = (m13 * m02 * m20 + m12 * m00 * m23 + m03 * m10 * m22 - m03 * m12 * m20 - m02 * m10 * m23 - m13 * m00 * m22) / det;
        a[8] = (m10 * m21 - m11 * m20) / det;
        a[9] = -(m00 * m21 - m01 * m20) / det;
        a[10] = (m11 * m00 - m01 * m10) / det;
        a[11] = -(m13 * m01 * m20 + m11 * m00 * m23 + m03 * m10 * m21 - m03 * m11 * m20 - m01 * m10 * m23 - m13 * m00 * m21) / det;
        a[15] = 1.0;
        return a;
    }

    /// <summary>operator*, with the homogeneous division.</summary>
    private static double[] Apply(double[] a, double[] p)
    {
        double x = p[0], y = p[1], z = p[2];
        double w = x * a[12] + y * a[13] + z * a[14] + a[15];
        return new[]
        {
            (y * a[1] + x * a[0] + z * a[2] + a[3]) / w,
            (x * a[4] + y * a[5] + z * a[6] + a[7]) / w,
            (x * a[8] + y * a[9] + z * a[10] + a[11]) / w,
        };
    }

    private static double[] TransformPoint(double[] p, double[] m)
    {
        double x = p[0], y = p[1], z = p[2];
        double v6 = x * m[12] + y * m[13] + z * m[14] + m[15];
        double v7 = x * m[4], v9 = z * m[10], v10 = z * m[6];
        double v11 = x * m[8] + y * m[9] + v9 + m[11];
        return new[] { (y * m[1] + x * m[0] + z * m[2] + m[3]) / v6, (y * m[5] + v7 + v10 + m[7]) / v6, v11 / v6 };
    }

    private static double[] TransformVector(double[] v, double[] m)
    {
        double x = v[0], y = v[1], z = v[2];
        double v6 = y * m[9], v7 = y * m[5], v9 = z * m[10], v10 = z * m[6];
        return new[] { y * m[1] + x * m[0] + z * m[2], x * m[4] + v7 + v10, x * m[8] + v6 + v9 };
    }

    private static (double X, double Y) Normalize2((double X, double Y) v)
    {
        double l = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (l <= 1e-300) return v;
        double r = 1.0 / l;
        return (v.X * r, v.Y * r);
    }

    /// <summary>
    /// The circle of three points in the plane: intersection of the two perpendicular bisectors, the unit vector from the
    /// centre to the first point, the sweep from the first to the third point.
    /// </summary>
    private static bool Circle2d(double[] q1, double[] q2, double[] q3, out Circle2 c)
    {
        c = default;
        double a2x = q1[0], a2y = q1[1], a3x = q2[0], a3y = q2[1], a4x = q3[0], a4y = q3[1];
        double v9 = a3y, v10 = a2y, v11 = a3x;
        double v12 = a3x - a2x;
        double p1x = v12 * 0.5 + a2x, p1y = v10 + (v9 - v10) * 0.5;
        double v13 = a4y - v9, v14 = a4x - v11;
        double p2x = v14 * 0.5 + v11, p2y = v9 + v13 * 0.5;
        var d1 = Normalize2((-(v9 - v10), v12));
        var d2 = Normalize2((-v13, v14));
        double det = d1.X * d2.Y - d2.X * d1.Y;
        if (!(det > Tol || -Tol > det)) return false;
        double tx = p2x - p1x, ty = p2y - p1y;
        double v13b = tx * d2.Y - ty * d2.X;
        double cy = d1.Y * v13b / det + p1y, cx = d1.X * v13b / det + p1x;
        double rx = a2x - cx, ry = a2y - cy;
        double v18 = a4x - cx, v17y = a4y - cy;
        double radius = Math.Sqrt(rx * rx + ry * ry);
        double ir = 1.0 / radius;
        rx *= ir; ry *= ir;
        double v19 = a2y - a4y, v20 = a2x - a4x, v21 = a3y - a2y, v22 = a3x - a2x;
        double nzv = v21 * v20 - v22 * v19;
        double nl = Math.Sqrt(nzv * nzv);
        double nz = nl > 1e-300 ? nzv * (1.0 / nl) : nzv;
        double perpX = 0.0 * 0.0 - nz * ry, perpY = nz * rx - 0.0 * 0.0;
        double v24 = v17y * rx, v25 = ry;
        double cr = v18 * ry - v17y * rx;
        double v26 = (v17y * 0.0 - ry * 0.0) * (v17y * 0.0 - ry * 0.0) + (rx * 0.0 - v18 * 0.0) * (rx * 0.0 - v18 * 0.0) + cr * cr;
        double v27 = v17y * ry + v18 * rx;
        double sweep = Math.Abs(Math.Atan2(Math.Sqrt(v26), v27));
        if ((v24 - v18 * v25) * nz + 0.0 + 0.0 < 0.0) sweep = TwoPi - sweep;
        c = new Circle2(cx, cy, rx, ry, perpX, perpY, radius, sweep);
        return true;
    }
}
