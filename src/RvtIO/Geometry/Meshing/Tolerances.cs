namespace RvtIO.Geometry.Meshing;

internal static class Tolerances
{
    internal static double SurfaceTolerance(double lod) => lod >= 0.5 ? 0.0001988 - 0.000198 * lod : 0.09998 - 0.1998 * lod;

    /// <summary>Longest facet edge Lmax = D * 10 / (1 + 50 lod), D = diagonal of the extents of the geometry.</summary>
    internal static double MaxEdgeLength(double extentsDiagonal, double lod) => extentsDiagonal * 10.0 / (lod * 50.0 + 1.0);

    /// <summary>Diagonal of the extents of a point set (the caller supplies the extents of the geometry the face belongs to).</summary>
    internal static double ExtentsDiagonal(double[] min, double[] max)
    {
        double dx = max[0] - min[0], dy = max[1] - min[1], dz = max[2] - min[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
