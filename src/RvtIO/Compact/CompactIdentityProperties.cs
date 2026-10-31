using RvtIO.Model;

namespace RvtIO.Compact;

/// <summary>
/// The "Invalid group" entries of a node that describe the element itself: Category, Family Name, and for an element with a type
/// Family, Type, Type Id and Family and Type. They are filed under "Invalid group" because the definitions of these built-in
/// parameters carry no parameter group.
/// <para>
/// Category and Family Name are listed for every element (the list always starts with the five default ids). Whether the four type
/// entries are listed is decided by the parameter list, which this class does not model: the caller supplies the listed ids, and an
/// unknown list writes no type entry. An entry whose value is not validated is not written.
/// </para>
/// </summary>
internal sealed class CompactIdentityProperties : ICompactPropertySource
{
    internal const string Group = CompactIdentity.InvalidGroup;

    private const int CategoryMt = -1140363;
    private const int FamilyNameId = -1002002;
    private const int TypeId = -1002000;
    private const int Type = -1002050;
    private const int Family = -1002051;
    private const int FamilyAndType = -1002052;

    private readonly CompactIdentity _identity;
    private readonly Func<Element, IReadOnlyCollection<int>?>? _listedIds;

    /// <param name="identity">The identity rules.</param>
    /// <param name="listedIds">The built-in parameter ids listed for an element, null when unknown (for example
    /// <see cref="CompactParameterLists.GetBuiltinIds"/>); without it no type entry is written.</param>
    public CompactIdentityProperties(CompactIdentity identity, Func<Element, IReadOnlyCollection<int>?>? listedIds = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _listedIds = listedIds;
    }

    public IReadOnlyList<CompactProperty> GetProperties(Element element)
    {
        var entries = GetEntries(element);
        var properties = new List<CompactProperty>(entries.Count);
        foreach (var (_, property) in entries) properties.Add(property);
        return properties;
    }

    /// <summary>The entries with the built-in parameter id they come from, in ascending id order. A caller that merges the entries
    /// of several sources must order them by id: when two parameters of a group have the same label the smallest id wins
    /// (Viewport: the view's own "Family", "Type" and "Family and Type" win over the ones listed here).</summary>
    public IReadOnlyList<(int Id, CompactProperty Property)> GetEntries(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var entries = new List<(int, CompactProperty)>(6);
        IReadOnlyCollection<int>? listed = _listedIds?.Invoke(element);
        // one text is shared by the four type entries, and by the two ids of the category
        CompactText category = _identity.CategoryValue(element);
        if (category.IsKnown) entries.Add((CategoryMt, new CompactProperty(Group, "Category", category.Value!)));
        CompactText typeValue = default;
        bool typeValueRead = false;
        foreach (int id in new[] { FamilyAndType, Family, Type })
            AddTypeEntry(id);
        if (_identity.FamilyNameParameter(element) is { IsKnown: true } familyName)
            entries.Add((FamilyNameId, new CompactProperty(Group, "Family Name", familyName.Value!)));
        AddTypeEntry(TypeId);
        return entries;

        void AddTypeEntry(int id)
        {
            if (listed is null || !listed.Contains(id)) return;
            if (!typeValueRead) { typeValue = _identity.TypeValue(element); typeValueRead = true; }
            if (typeValue.IsKnown) entries.Add((id, new CompactProperty(Group, Label(id), typeValue.Value!)));
        }
    }

    private static string Label(int id) => id switch
    {
        TypeId => "Type Id",
        Type => "Type",
        Family => "Family",
        _ => "Family and Type",
    };
}
