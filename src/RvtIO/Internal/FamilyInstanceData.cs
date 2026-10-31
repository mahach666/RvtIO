namespace RvtIO.Internal;

/// <summary>
/// Raw FamilyInstance transform data extracted from 0x0702 blocks.
/// Trf: 3x4 transform matrix at offset 344, rotation part ROW-major.
/// Layout: [Row0(3d), Row1(3d), Row2(3d), Translation(3d)] = 12 doubles.
/// Basis vectors are the matrix columns, i.e. BasisX = (Trf[0], Trf[3], Trf[6]).
/// After Trf: SymbolId(i32) at offset 440.
/// </summary>
internal record FamilyInstanceData
{
    /// <summary>3x4 transform matrix as 12 doubles, rotation rows first:
    /// [m00,m01,m02, m10,m11,m12, m20,m21,m22, tx,ty,tz]</summary>
    public double[] Trf { get; set; } = new double[12];

    /// <summary>Family symbol (type) element ID from offset 440.</summary>
    public int SymbolId { get; set; }

    /// <summary>Translation component [x, y, z] in feet (last 3 doubles of Trf).</summary>
    public double[] Translation => [Trf[9], Trf[10], Trf[11]];

    /// <summary>True if the rotation part is identity (no rotation).</summary>
    public bool IsIdentityRotation
    {
        get
        {
            const double eps = 1e-6;
            return Math.Abs(Trf[0] - 1) < eps && Math.Abs(Trf[1]) < eps && Math.Abs(Trf[2]) < eps
                && Math.Abs(Trf[3]) < eps && Math.Abs(Trf[4] - 1) < eps && Math.Abs(Trf[5]) < eps
                && Math.Abs(Trf[6]) < eps && Math.Abs(Trf[7]) < eps && Math.Abs(Trf[8] - 1) < eps;
        }
    }
}
