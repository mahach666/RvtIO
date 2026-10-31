using RvtIO.Model;

namespace RvtIO.Compact;

/// <summary>One printed property of a compact JSON node: the group block it is listed under, its label, and its value text.</summary>
internal readonly record struct CompactProperty(string Group, string Label, string Value);

/// <summary>Supplies the property list of an element.</summary>
internal interface ICompactPropertySource
{
    IReadOnlyList<CompactProperty> GetProperties(Element element);
}
