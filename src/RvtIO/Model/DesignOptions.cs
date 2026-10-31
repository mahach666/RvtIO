namespace RvtIO.Model;
internal sealed class DesignOptions
{
    private readonly Dictionary<int, int> _primaryOfSet = new();
    private readonly Dictionary<int, int> _setOfOption = new();
    private readonly Dictionary<int, string> _names = new();
    private readonly HashSet<int> _conflicting = new();

    internal static DesignOptions Of(IEnumerable<Element> elements)
    {
        var options = new DesignOptions();
        foreach (var element in elements)
        {
            if (element.NativePrimaryDesignOptionId is int primary) options.Add(options._primaryOfSet, element.Id, primary);
            if (element.NativeDesignOptionSetId is int set) options.Add(options._setOfOption, element.Id, set);
            if (element.NativeDesignOptionName is { } name && (element.NativePrimaryDesignOptionId != null || element.NativeDesignOptionSetId != null))
                options.Add(options._names, element.Id, name);
        }
        return options;
    }
    private void Add<T>(Dictionary<int, T> map, int id, T value)
    {
        if (!map.TryAdd(id, value) && !EqualityComparer<T>.Default.Equals(map[id], value)) _conflicting.Add(id);
    }
    internal bool? InNonPrimaryOption(Element element)
    {
        if (element.NativeHeaderDesignOptionId is not int option) return null;
        if (option == -1) return false;
        if (_conflicting.Contains(option) || !_setOfOption.TryGetValue(option, out int set)) return null;
        if (_conflicting.Contains(set) || !_primaryOfSet.TryGetValue(set, out int primary)) return null;
        return option != primary;
    }
    internal string? NameOf(int id) => !_conflicting.Contains(id) && _names.TryGetValue(id, out string? name) ? name : null;
    internal int? SetOf(int optionId) => !_conflicting.Contains(optionId) && _setOfOption.TryGetValue(optionId, out int set) ? set : null;
    internal int? PrimaryOf(int setId) => !_conflicting.Contains(setId) && _primaryOfSet.TryGetValue(setId, out int primary) ? primary : null;
}
