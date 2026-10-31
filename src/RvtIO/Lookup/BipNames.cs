namespace RvtIO.Lookup;

internal static class BipNames
{
    public static readonly Dictionary<int, string> Lookup = LoadLookup();

    static Dictionary<int, string> LoadLookup()
    {
        var dict = new Dictionary<int, string>();
        foreach (BuiltIns.BuiltInParameter val in Enum.GetValues<BuiltIns.BuiltInParameter>())
        {
            int key = (int)val;
            if (!dict.ContainsKey(key))
                dict[key] = val.ToString();
        }
        dict.TryAdd(-1001111, "WALL_BASE_OFFSET_COMPUTED");
        dict.TryAdd(-1001101, "WALL_HEIGHT_PARAM");
        return dict;
    }
}
