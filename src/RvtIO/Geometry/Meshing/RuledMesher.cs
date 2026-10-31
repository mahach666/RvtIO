namespace RvtIO.Geometry.Meshing;

internal static class RuledMesher
{
    /// <summary>Meshes <paramref name="face"/> (SurfaceKind Ruled); the adjacent freeform faces of the solid come from face.Solid.</summary>
    internal static bool TryMesh(BrFace face, double lod, double extentsDiagonal, out Mesh mesh)
    {
        mesh = null!;
        if (face.Ruled == null) return false;
        return FreeformSolid.TryMeshFace(face, lod, extentsDiagonal, out mesh);
    }
}
