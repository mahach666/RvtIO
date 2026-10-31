namespace RvtIO.Geometry.Meshing;

internal static class FaceMesher
{
    /// <summary>
    /// Meshes one B-rep face. <paramref name="extentsDiagonal"/> is the diagonal D of the extents of the geometry the face
    /// belongs to (Lmax = D * 10 / (1 + 50 lod)).
    /// </summary>
    internal static bool TryMesh(BrFace face, double lod, double extentsDiagonal, out Mesh mesh)
    {
        mesh = null!;
        if (!double.IsFinite(lod) || lod <= 0.0 || !double.IsFinite(extentsDiagonal) || extentsDiagonal <= 0.0) return false;
        switch (face.SurfaceKind)
        {
            case SurfaceKind.Plane:
                return PlanarMesher.TryMesh(face, Tolerances.MaxEdgeLength(extentsDiagonal, lod), out mesh);
            case SurfaceKind.Cylinder:
            case SurfaceKind.Cone:
                return CylinderMesher.TryMesh(face, Tolerances.SurfaceTolerance(lod), Tolerances.MaxEdgeLength(extentsDiagonal, lod), out mesh);
            case SurfaceKind.Revolved:
                return RevolvedMesher.TryMesh(face, Tolerances.SurfaceTolerance(lod), Tolerances.MaxEdgeLength(extentsDiagonal, lod), out mesh);
            case SurfaceKind.Ruled:
                return RuledMesher.TryMesh(face, lod, extentsDiagonal, out mesh);
            case SurfaceKind.NurbsSurface:
                return NurbsSurfaceMesher.TryMesh(face, lod, extentsDiagonal, out mesh);
            default:
                return false;
        }
    }
}
