using RvtIO.Model;
using RvtIO.Parsing;

namespace RvtIO.Compact;

/// <summary>
/// Facts of the document that decide parameter lists; null stands for "not known". <c>IsWorkshared</c>: the document is
/// workshared.
/// </summary>
internal sealed record CompactListFileFacts(bool? IsWorkshared, bool? IsProjectDocument, bool? DesignOptionSetsExist, DesignOptions? DesignOptions = null)
{
    internal static readonly CompactListFileFacts Unknown = new(null, null, null);
}

internal sealed class ElementListFacts(Element element, int? headerCategory, CompactListFileFacts file, Func<int, Element?>? resolve = null)
    : ICompactListFacts
{
    private static readonly Lazy<Dictionary<string, int>> ClassIndexByName = new(() =>
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (index, schema) in TfBlockDeserializer.Schemas) map[schema.Name] = index;
        return map;
    });

    public IReadOnlyCollection<int>? StoredBuiltinIds =>
        element.NativeParameterStoreIds is { } ids && element.NativeParameterStoresComplete ? ids : null;

    /// <summary>
    /// Ids of the built-in items of the FamilyParams of the element (or of its master symbol) that its list merges: the items
    /// marked as instance parameters when <paramref name="instanceItems"/>, the other items when <paramref name="otherItems"/>.
    /// </summary>
    public IReadOnlyCollection<int>? FamilyParameterIds(bool symbol, bool instanceItems, bool otherItems)
    {
        Element? owner = element;
        if (symbol)
        {
            if (!CompactFamilyInstanceFacts.Applies(element) || resolve is null || Field(element, TfClassNames.Key("FamilyInstance", "m_masterSymbolId")) is not int symbolId
                || symbolId == -1) return null;
            owner = resolve(symbolId);
        }
        if (owner is null || !owner.NativeTailWalkComplete) return null;
        var ids = new List<int>();
        foreach (var (id, instance) in owner.NativeNamedParameters ?? [])
            if (instance ? instanceItems : otherItems) ids.Add(id);
        return ids;
    }

    public int? Get(string fact)
    {
        switch (fact)
        {
            case "headerCategory": return headerCategory;
            case "famId": return element.SerializedFamilyId;
            case "isInAFamily": return element.SerializedFamilyId is int family ? (family == -1 ? 0 : 1) : null;
            case "unplacedOwnerId": return element.SerializedUnplacedOwnerId;
            case "ownerViewId": return element.SerializedOwnerDbViewId;
            case "createdPhaseId": return element.SerializedCreatedPhaseId;
            case "cellList": return element.NativeCellListTag;
            case "docWorkshared": return file.IsWorkshared is bool shared ? (shared ? 1 : 0) : null;
            case "docOwnerFamilyId": return file.IsProjectDocument == true ? -1 : null;
            case "docDesignOptionSets": return file.DesignOptionSetsExist is bool exist ? (exist ? 1 : 0) : null;
            case "docDesignOptionSetsEmpty": return file.DesignOptionSetsExist is bool any ? (any ? 0 : 1) : null;
            // every element of a loaded document belongs to its database
            case "elementDatabase": return 1;
            case "dimStyleSymbolId": return Field(element, DimensionStyleSymbol);
            case "dimSketchPlaneId": return Field(element, TfClassNames.Key("Dimension", "m_dimSketchPlaneId"));
            case "dimShowLabel": return Field(element, TfClassNames.Key("Dimension", "m_showLabelGP"));
            case "dimFlagsMask64": return Field(element, TfClassNames.Key("Dimension", "m_flags")) is int flags ? flags & 64 : null;
            case "dimStyleType": return StyleField(TfClassNames.Key("DimensionStyle", "m_dimensionStyleType"));
            case "dimStyleLinearType": return StyleField(TfClassNames.Key("DimensionStyle", "m_linearDimType"));
            case "typeBodyCategory": return CompactFamilyInstanceFacts.Applies(element) ? CompactFamilyInstanceFacts.SymbolCategory(element, resolve) : null;
            case "typeBodyCategoryHasPhases":
                return CompactFamilyInstanceFacts.Applies(element) && CompactFamilyInstanceFacts.SymbolCategory(element, resolve) is int typeCategory
                    && CompactParameterLists.CategoryHasPhases(typeCategory) is bool typePhases ? (typePhases ? 1 : 0) : null;
        }
        if (fact.StartsWith("fi:", StringComparison.Ordinal) || fact.StartsWith("fs:", StringComparison.Ordinal)
            || fact.StartsWith("unmodelled:getMemberIndexToTagMap", StringComparison.Ordinal))
            return CompactFamilyInstanceFacts.Get(fact, element, resolve);
        // a scalar schema field of the element's own record (see TfBlockDeserializer.ListFactFieldKeys)
        if (fact.StartsWith("field:", StringComparison.Ordinal)) return Field(element, fact["field:".Length..]);
        if (fact.StartsWith("cellHas:", StringComparison.Ordinal)) return CellPresent(fact["cellHas:".Length..]);
        if (fact.StartsWith("stored:", StringComparison.Ordinal))
            return StoredBuiltinIds is { } stored && int.TryParse(fact["stored:".Length..], out int id)
                ? (stored.Contains(id) ? 1 : 0) : null;
        if (fact.StartsWith("catHasPhases:", StringComparison.Ordinal) && int.TryParse(fact["catHasPhases:".Length..], out int category))
            return CompactParameterLists.CategoryHasPhases(category) is bool phases ? (phases ? 1 : 0) : null;
        return null;
    }

    /// <summary>The class name of the element's record: the Tf class name; null when the record class is not known.</summary>
    internal static string? RecordClassName(Element element) =>
        element.NativeRootClassName is { } nativeName ? nativeName :
        element.NativeRecordClass is int tag && TfBlockDeserializer.Schemas.TryGetValue(tag, out var schema) ? schema.Name : null;

    private static readonly string DimensionStyleSymbol = TfClassNames.Key("Dimension", "m_styleSymbolId");

    private static int? Field(Element e, string key) =>
        e.NativeListFields is { } fields && fields.TryGetValue(key, out int value) ? value : null;

    /// <summary>A field of the dimension style the element refers to.</summary>
    private int? StyleField(string key) =>
        Field(element, DimensionStyleSymbol) is int id && id != -1 && resolve?.Invoke(id) is { } style ? Field(style, key) : null;

    /// <summary>1 when the element's cell list holds a cell of the named Tf class, 0 when it does not, null when unknown.</summary>
    private int? CellPresent(string className)
    {
        if (element.NativeCellListTag == 0) return 0;
        if (element.NativeCellListTag is null || element.NativeCellClasses is not { } cells) return null;
        // a class the file schema does not declare cannot occur in the file
        if (!ClassIndexByName.Value.TryGetValue(className, out int index)) return 0;
        return Array.IndexOf(cells, index) >= 0 ? 1 : 0;
    }
}
