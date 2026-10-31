using System.Globalization;
using RvtIO.Model;
using RvtIO.Parsing;

namespace RvtIO.Compact;

/// <param name="Family">True for a parameter the family defines (a ParamElemFamily), false for a project or shared parameter.</param>
internal readonly record struct CompactUserEntry(int ParameterId, string Group, string Label, string? Text, double? Raw, string? Spec, bool Family = false, string Source = "");

internal sealed class CompactUserParameters : ICompactPropertySource
{
    private readonly UserParameterCatalog? _catalog;
    private readonly Func<string, double, string?>? _formatNumber;
    private readonly Func<int, string?> _elementName;
    private readonly Dictionary<int, Element> _byId;

    public CompactUserParameters(RevitFileInfo info, Func<string, double, string?>? formatNumber = null, Func<int, string?>? elementName = null)
    {
        _catalog = info.UserParameters;
        _byId = info.Elements.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        _formatNumber = formatNumber;
        if (elementName == null)
        {
            var names = new Dictionary<int, string?>();
            foreach (var element in info.Elements)
                if (element.RuleClassName == "MaterialElem") names.TryAdd(element.Id, element.SerializedMaterialName);
            elementName = id => names.GetValueOrDefault(id);
        }
        _elementName = elementName;
    }
    internal Action<Element, UserParameterEntry>? Trace { get; set; }

    public IReadOnlyList<CompactUserEntry> GetEntries(Element element)
    {
        if (_catalog == null) return [];
        var result = new List<CompactUserEntry>();
        var source = CompactIfcParameters.CenterLineClasses.Contains(ElementListFacts.RecordClassName(element) ?? "")
            && element.SerializedOwningElementId is int owner && _byId.TryGetValue(owner, out var ownerElement) ? ownerElement : element;
        foreach (var entry in _catalog.EntriesOf(source))
        {
            var d = entry.Definition;
            if (UserParameterCatalog.GroupLabelOf(d) is not { } group) continue;
            if (d.IsImage && entry.Value is { ElementId: > 0 }) continue;
            Trace?.Invoke(element, entry);
            if (entry.Value is not { } v || d.IsImage)
            {
                if (entry.Value is null && !d.IsImage && (!source.NativeParameterStoresComplete || !_catalog.ValuesKnown(source))) continue;
                result.Add(new(d.Id, group, d.Caption, "", null, d.Spec, d.Instance != null));
                continue;
            }
            switch (d.Storage)
            {
                case StorageType.String:
                    result.Add(new(d.Id, group, d.Caption, v.Text ?? "", null, d.Spec, d.Instance != null));
                    break;
                case StorageType.Integer:
                    result.Add(new(d.Id, group, d.Caption,
                        d.IsYesNo ? (v.Integer != 0 ? "Yes" : "No") : v.Integer.ToString(CultureInfo.InvariantCulture), null, d.Spec, d.Instance != null));
                    break;
                case StorageType.ElementId:
                    if (v.ElementId == -1) result.Add(new(d.Id, group, d.Caption, "None", null, d.Spec, d.Instance != null));
                    else if (v.ElementId > 0 && _elementName(v.ElementId) is { Length: > 0 } name)
                        result.Add(new(d.Id, group, d.Caption, name, null, d.Spec, d.Instance != null));
                    break;
                case StorageType.Double:
                    result.Add(new(d.Id, group, d.Caption, null, v.Number, d.Spec, d.Instance != null));
                    break;
            }
        }
        return result;
    }

    public IReadOnlyList<CompactProperty> GetProperties(Element element) => GetPropertiesWithKind(element).Select(p => p.Property).ToList();

    /// <summary>The printed properties with the kind of their parameter (true: defined by the family).</summary>
    internal IReadOnlyList<(CompactProperty Property, bool Family)> GetPropertiesWithKind(Element element)
    {
        var result = new List<(CompactProperty, bool)>();
        foreach (var e in GetEntries(element))
        {
            string? text = e.Text ?? (e.Raw is double raw && e.Spec != null ? _formatNumber?.Invoke(e.Spec, raw) : null);
            if (text != null) result.Add((new CompactProperty(e.Group, e.Label, text), e.Family));
        }
        return result;
    }
}
