using System.Globalization;
using RvtIO.Lookup;

namespace RvtIO.Compact;

internal static class CompactValueRules
{
    /// <summary>ELEM_CATEGORY_PARAM and ELEM_CATEGORY_PARAM_MT: both print the header category of the element.</summary>
    internal const int ElemCategoryParam = -1140362;
    internal const int ElemCategoryParamMt = -1140363;

    /// <summary>The versionless data type of the boolean spec (autodesk.spec:spec.bool-1.0.0).</summary>
    internal const string BoolSpec = "autodesk.spec:spec.bool";

    internal static readonly IReadOnlySet<int> ImageParameters = new HashSet<int> { -1152385, -1152384 };

    /// <summary>ALL_MODEL_TYPE_NAME ("Type Name") and ALL_MODEL_FAMILY_NAME ("Family Name").</summary>
    internal const int TypeNameParameter = -1002001;
    internal const int FamilyNameParameter = -1002002;

    internal static bool IsCategoryParameter(int id) => id is ElemCategoryParam or ElemCategoryParamMt;

    /// <summary>"Yes" / "No" for the boolean spec (a non-zero value is Yes), the decimal text of the integer otherwise.</summary>
    internal static string IntegerText(int value, string spec) =>
        IsBoolSpec(spec) ? (value != 0 ? "Yes" : "No") : value.ToString(CultureInfo.InvariantCulture);

    internal static bool IsBoolSpec(string spec) => StripVersion(spec) == BoolSpec;

    /// <summary>
    /// The text of an element id value of an ordinary ElementId parameter: "None" for -1, the signed decimal for another
    /// negative id, and <paramref name="nameOf"/> of a positive id. The id 0 is not decided.
    /// </summary>
    internal static string? ElementIdText(int id, Func<int, string?> nameOf) =>
        id == -1 ? "None" : id < 0 ? id.ToString(CultureInfo.InvariantCulture) : id > 0 ? nameOf(id) : null;

    /// <summary>
    /// The value of the category parameters from the header category of the element: "None" for the null id, the
    /// BuiltInCategory enum name for a built-in (negative) category, the decimal id for a positive id.
    /// </summary>
    internal static string? CategoryText(int? headerCategory)
    {
        if (headerCategory is not int id) return null;
        if (id == -1) return "None";
        if (id < 0) return BuiltInCategoryNames.Lookup.GetValueOrDefault(id);
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static string StripVersion(string spec)
    {
        int dash = spec.IndexOf('-');
        return dash < 0 ? spec : spec[..dash];
    }
}
