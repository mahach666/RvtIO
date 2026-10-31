namespace RvtIO.Geometry.Meshing;

internal static class NurbsSurfaceMesher
{
    /// <summary>Meshes <paramref name="face"/> (SurfaceKind NurbsSurface); the adjacent freeform faces of the solid come from face.Solid.</summary>
    internal static bool TryMesh(BrFace face, double lod, double extentsDiagonal, out Mesh mesh)
    {
        mesh = null!;
        if (face.Nurbs == null) return false;
        return FreeformSolid.TryMeshFace(face, lod, extentsDiagonal, out mesh);
    }
}
