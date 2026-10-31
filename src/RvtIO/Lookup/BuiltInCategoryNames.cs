namespace RvtIO.Lookup;

internal static class BuiltInCategoryNames
{
    public static readonly Dictionary<int, string> Lookup = LoadLookup();

    static Dictionary<int, string> LoadLookup()
    {
        var dict = new Dictionary<int, string>();
        foreach (BuiltIns.BuiltInCategory val in Enum.GetValues<BuiltIns.BuiltInCategory>())
        {
            int key = (int)val;
            if (!dict.ContainsKey(key))
                dict[key] = val.ToString();
        }
        foreach (var (id, name) in BuiltInCategoryNamesAfter2022.Added) dict.TryAdd(id, name);
        return dict;
    }
}
