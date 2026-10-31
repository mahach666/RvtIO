namespace RvtIO.Model;

/// <summary>
/// A single layer in a CompoundStructure (wall/floor/ceiling type).
/// </summary>
public class MaterialLayer
{
    /// <summary>Layer thickness in millimeters.</summary>
    public double ThicknessMm { get; set; }

    /// <summary>Material function: Structure, Substrate, ThermalAir, Finish, Membrane.</summary>
    public string? Function { get; set; }

    /// <summary>Material element ID (0 if none).</summary>
    public int MaterialId { get; set; }

    /// <summary>Material element name (resolved from MaterialId).</summary>
    public string? MaterialName { get; set; }

    /// <summary>Layer index (0-based, from exterior to interior).</summary>
    public int LayerIndex { get; set; }
}
