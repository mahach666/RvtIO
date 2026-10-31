using RvtIO.Model;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>
    /// Gives each instance that has one (<see cref="Element.SerializedComputedSymbolGeometry"/>) the geometry of its computed
    /// symbol and returns what to put back after the export.
    /// </summary>
    internal static List<(Element Element, GeometryData? Original)> UseComputedSymbolGeometryCurrent(IEnumerable<Element> elements)
    {
        var swapped = new List<(Element, GeometryData?)>();
        foreach (var element in elements)
        {
            if (element.SerializedComputedSymbolGeometry is not { } geometry) continue;
            swapped.Add((element, element.Geometry));
            element.Geometry = geometry;
        }
        return swapped;
    }
}
