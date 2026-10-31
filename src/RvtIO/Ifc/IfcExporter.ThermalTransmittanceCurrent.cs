using System.Globalization;
using RvtIO.Model;

namespace RvtIO.Ifc;

/// <summary>Pset_*Common.ThermalTransmittance of the current profile (the property maps to
/// ANALYTICAL_HEAT_TRANSFER_COEFFICIENT). The parameter is not stored on a wall or floor type: it is derived from the
/// type's compound structure as 1 / sum(layer width / thermal conductivity of the layer material), a layer whose
/// material has no thermal asset adding nothing and a type without any such layer having no value. Widths are in
/// feet and the conductivity of the asset is stored as W/(m K) / 0.3048, so the quotient is already W/(m2 K).</summary>
public partial class IfcExporter
{
    /// <summary>THERMAL_MATERIAL_PARAM_EMISSIVITY: stored by thermal assets only (a structural asset has the
    /// PHY_MATERIAL_PARAM_* set), so it tells the two PropertySetElement kinds of a material apart.</summary>
    private const int ThermalAssetEmissivityParamId = -1152320;
    private const int ThermalConductivityParamId = -1152308;
    private const int PropertySetElementCategoryId = -2000924;
    private const ushort BasicWallTypeClassId = 512, FloorTypeClassId = 1915;

    /// <summary>The thermal conductivity (file units) of the thermal asset a material refers to; null when the
    /// material refers to none or to several.</summary>
    internal static double? ResolveMaterialThermalConductivityCurrent(
        Element material, IReadOnlyDictionary<int, Element> elementsById)
    {
        double? found = null;
        foreach (int id in material.References ?? [])
        {
            if (id == material.Id || !elementsById.TryGetValue(id, out Element? asset)
                || asset.CategoryId != PropertySetElementCategoryId
                || asset.Parameters.All(parameter => parameter.Id != ThermalAssetEmissivityParamId))
                continue;
            if (found != null) return null;
            if (asset.Parameters.FirstOrDefault(parameter => parameter.Id == ThermalConductivityParamId)?.Value
                is not double conductivity || !double.IsFinite(conductivity))
                return null;
            found = conductivity;
        }
        return found;
    }

    /// <summary>The transmittance of a basic wall or floor type in W/(m2 K); +infinity when every layer that has a
    /// thermal asset is zero wide (the division by a zero resistance), null when no layer has one. A layer width or
    /// conductivity that cannot be read gives null. <paramref name="detail"/> collects the layers for RVTIO_PSET_DIAG.</summary>
    internal static double? ResolveHostTypeThermalTransmittanceCurrent(
        Element type, IReadOnlyDictionary<int, Element> elementsById, List<string>? detail = null)
    {
        if (type.SerializedSystemTypeClassId is not (BasicWallTypeClassId or FloorTypeClassId)
            || type.SerializedHostTypeLayers is not { Length: > 0 } layers)
            return null;
        double resistance = 0;
        bool hasThermalLayer = false;
        foreach (var layer in layers)
        {
            double widthFeet = BitConverter.Int64BitsToDouble(layer.LayerWidthBits);
            Element? material = elementsById.GetValueOrDefault(layer.RawMaterialId);
            double? conductivity = material == null ? null : ResolveMaterialThermalConductivityCurrent(material, elementsById);
            detail?.Add($"{widthFeet.ToString("R", CultureInfo.InvariantCulture)}/m{layer.RawMaterialId}/l{conductivity?.ToString("R", CultureInfo.InvariantCulture) ?? "-"}");
            if (!double.IsFinite(widthFeet) || widthFeet < 0) return null;
            if (conductivity is null) continue;
            if (conductivity <= 0) return null;
            hasThermalLayer = true;
            resistance += widthFeet / conductivity.Value;
        }
        return hasThermalLayer ? 1.0 / resistance : null;
    }

    /// <summary>The transmittance of an occurrence of a wall or floor: that of its own type, which carries the
    /// compound structure (a family instance has none of its own).</summary>
    internal static double? ResolveOccurrenceThermalTransmittanceCurrent(
        Element occurrence, IReadOnlyDictionary<int, Element> elementsById)
    {
        if (occurrence.SerializedSystemTypeId is not > 0 || occurrence.SerializedSystemTypeId == occurrence.Id
            || !elementsById.TryGetValue(occurrence.SerializedSystemTypeId.Value, out Element? type)
            || type.CategoryId != occurrence.CategoryId && !(type.CategoryId == -2001300 && occurrence.CategoryId == -2000032))
            return null;
        return ResolveHostTypeThermalTransmittanceCurrent(type, elementsById);
    }

    /// <summary>The literal of the property; an infinite value is written as <c>Infinity.</c>.</summary>
    internal static string ThermalTransmittanceLiteralCurrent(double transmittance) =>
        $"IFCTHERMALTRANSMITTANCEMEASURE({D17(transmittance)})";

    /// <summary>RVTIO_PSET_DIAG: one line per host type with a compound structure: id, category, name, layers
    /// (width, material id, conductivity) and the computed transmittance.</summary>
    private static void WriteThermalDiagCurrent(string path, IReadOnlyDictionary<int, Element> elementsById)
    {
        var lines = new List<string>();
        foreach (Element type in elementsById.Values.Where(element => element.SerializedHostTypeLayers != null).OrderBy(element => element.Id))
        {
            var detail = new List<string>();
            double? transmittance = ResolveHostTypeThermalTransmittanceCurrent(type, elementsById, detail);
            lines.Add(string.Join(',', "Thermal", type.Id, type.CategoryId, type.SerializedSystemTypeClassId,
                type.Name?.Replace(',', ';') ?? "", string.Join('|', detail),
                transmittance?.ToString("R", CultureInfo.InvariantCulture) ?? "-"));
        }
        File.AppendAllLines(path, lines);
    }
}
