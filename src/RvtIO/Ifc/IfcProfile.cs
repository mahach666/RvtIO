namespace RvtIO.Ifc;

/// <summary>The IFC output dialect <see cref="IfcExporter"/> writes. The two profiles differ in details such as
/// storey GlobalIds, type names and which property sets are written; both stay fully supported.</summary>
public enum IfcProfile
{
    /// <summary>The older dialect.</summary>
    Legacy,

    /// <summary>The newer dialect, the default.</summary>
    Current,
}
