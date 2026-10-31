namespace RvtIO.Ifc;

/// <summary>The name-selection contract of the parameter lookup of the Revit IFC exporter.
/// Inputs must describe the complete public parameter enumeration, with known
/// definition/group/visibility and getter-derived HasValue. Persisted stores or
/// the approximate public Model.Parameter projection are not such a scope.
/// The caller must also supply the matching comparison backend:
/// Framework/NLS InvariantCultureIgnoreCase is not modern ICU equivalence.</summary>
internal sealed class IfcParameterNameCache
{
    internal const int IfcGroup = -5000131;
    internal const int DuplicateCategoryParameter = -1140363;
    internal const int DuplicateOriginalCategoryParameter = -1001125;

    internal sealed record DefinitionFact(string? Name, int GroupId,
        bool IsInternalDefinition, bool IsVisible);
    internal sealed record ParameterFact(int Id, DefinitionFact? Definition, bool HasValue);

    private readonly IComparer<string> nameComparer;
    private readonly SortedDictionary<string, ParameterFact> ifc;
    private readonly SortedDictionary<int, SortedDictionary<string, ParameterFact>> other = new();
    private SortedDictionary<string, ParameterFact> NewGroup() => new(nameComparer);

    private IfcParameterNameCache(IComparer<string> nameComparer)
    {
        this.nameComparer = nameComparer;
        ifc = NewGroup();
    }

    internal static IfcParameterNameCache? Create(IEnumerable<ParameterFact?>? parameters,
        IComparer<string>? sourceNameComparer, bool publicParameterScopeComplete, out string? failure)
    {
        failure = null;
        if (!publicParameterScopeComplete || parameters == null)
        {
            failure = "Complete public parameter/definition/HasValue scope unavailable";
            return null;
        }
        if (sourceNameComparer == null)
        {
            failure = "Source-compatible parameter name comparison unavailable";
            return null;
        }

        // First filter, then overwrite equal IDs, then visit
        // signed IDs in ascending order. Sorting names first is not equivalent.
        var stable = new SortedDictionary<int, ParameterFact>();
        foreach (var parameter in parameters)
        {
            if (parameter == null || parameter.Id is DuplicateCategoryParameter or DuplicateOriginalCategoryParameter)
                continue;
            var definition = parameter.Definition;
            if (definition == null || definition.IsInternalDefinition && !definition.IsVisible
                || string.IsNullOrWhiteSpace(definition.Name))
                continue;
            stable[parameter.Id] = parameter;
        }

        var result = new IfcParameterNameCache(sourceNameComparer);
        foreach (var parameter in stable.Values)
        {
            var definition = parameter.Definition!;
            SortedDictionary<string, ParameterFact> group;
            if (definition.GroupId == IfcGroup) group = result.ifc;
            else if (!result.other.TryGetValue(definition.GroupId, out group!))
            {
                group = result.NewGroup();
                result.other.Add(definition.GroupId, group);
            }
            string name = definition.Name!.Replace(" ", "", StringComparison.Ordinal);
            if (!group.TryGetValue(name, out var previous) || !previous.HasValue)
                group[name] = parameter;
        }
        return result;
    }

    /// <summary>Returns the selected candidate, even if HasValue is false.
    /// No other group is searched for a value in that case.</summary>
    internal ParameterFact? Find(string name, int? groupId = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        string cleanName = name.Replace(" ", "", StringComparison.Ordinal);
        if (groupId.HasValue)
        {
            var group = groupId == IfcGroup ? ifc : other.GetValueOrDefault(groupId.Value);
            return group?.GetValueOrDefault(cleanName);
        }
        if (ifc.TryGetValue(cleanName, out var match)) return match;
        foreach (var group in other.Values)
            if (group.TryGetValue(cleanName, out match)) return match;
        return null;
    }
}
