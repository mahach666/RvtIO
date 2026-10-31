using RvtIO.Model;
using RvtIO.Parsing;

namespace RvtIO.Compact;

/// <summary>Facts about what kind of element a node is, from its own Tf class.</summary>
internal static class CompactElementKinds
{
    /// <summary>True for a type element (its Tf class derives from <c>Symbol</c>), false for any other element; null when the reader
    /// has no class id for it.</summary>
    internal static bool? IsType(Element element) =>
        element.NativeIsSymbol is bool symbol ? symbol && element.NativeRootClassName != SegmentCenterLineClass :
        element.RuleClassName is { } className and not SegmentCenterLineClass ? element.RuleClassIs("Symbol") : null;

    /// <summary>SegmentCenterLine is not derived from Symbol, yet it has a type name and a family name ("Round Duct"): whether it is a
    /// type is not decided.</summary>
    private const string SegmentCenterLineClass = "SegmentCenterLine";
}
