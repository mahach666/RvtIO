using RvtIO.Parsing;

namespace RvtIO.Ifc;

/// <summary>The IFC length unit family the file declares, decided by the document's stored Length format option.</summary>
public enum IfcUnitFamily
{
    /// <summary>Lengths are written in millimetres.</summary>
    Millimetre,
    /// <summary>Lengths are written in metres.</summary>
    Metre,
    /// <summary>Lengths are written in feet.</summary>
    Foot,
}

internal static class IfcLengthUnitChoice
{
    private const string UnitPrefix = "autodesk.unit.unit:";

    /// <summary>The unit id of the single Length option, null when it is missing, ambiguous or the library default.</summary>
    internal static string? StoredLengthUnit(TfBlockDeserializer.UnitOptions? options)
    {
        if (options == null) return null;
        var matches = options.Options.Where(option =>
            UnitSchemaEvaluator.DisplayNameKey(option.Spec) == IfcNumericUnitPlan.LengthSpecName).ToArray();
        if (matches.Length != 1 || matches[0].UseDefault != 0 || string.IsNullOrEmpty(matches[0].Unit)) return null;
        return matches[0].Unit;
    }

    /// <summary>Feet and feet-and-fractional-inches are written as FOOT, meters as METRE, millimeters as MILLI METRE;
    /// every other or unreadable option keeps the millimetre output.</summary>
    internal static IfcUnitFamily Family(string? storedLengthUnit) =>
        storedLengthUnit == null ? IfcUnitFamily.Millimetre
        : UnitSchemaEvaluator.DisplayNameKey(storedLengthUnit) switch
        {
            UnitPrefix + "feet" or UnitPrefix + "feetFractionalInches" => IfcUnitFamily.Foot,
            UnitPrefix + "meters" => IfcUnitFamily.Metre,
            _ => IfcUnitFamily.Millimetre,
        };
}
