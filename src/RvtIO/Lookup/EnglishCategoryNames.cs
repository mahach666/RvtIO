using System.Globalization;

namespace RvtIO.Lookup;

/// <summary>The English names of the built-in categories (BuiltInCategory id to name; categories without a name are not listed).
/// A built-in category has no element in the file, so it is named from this table whatever the
/// language of the file; the exporter orders the material list of a family by these names.</summary>
internal static class EnglishCategoryNames
{
    private static readonly Lazy<IReadOnlyDictionary<int, string>> Names = new(Load);

    /// <summary>The name of the built-in category with the given id (a negative BuiltInCategory value); null when the category
    /// has none.</summary>
    internal static string? Get(int categoryId) => Names.Value.GetValueOrDefault(categoryId);

    private static IReadOnlyDictionary<int, string> Load()
    {
        using var stream = typeof(EnglishCategoryNames).Assembly
            .GetManifestResourceStream("RvtIO.Resources.english_category_names.tsv")
            ?? throw new InvalidOperationException("Missing resource english_category_names.tsv.");
        using var reader = new StreamReader(stream);
        var names = new Dictionary<int, string>();
        for (string? line; (line = reader.ReadLine()) != null;)
        {
            int tab = line.IndexOf('\t');
            if (tab <= 0 || !int.TryParse(line.AsSpan(0, tab), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id))
                throw new InvalidDataException($"Malformed line in english_category_names.tsv: {line}");
            names.Add(id, line[(tab + 1)..]);
        }
        return names;
    }
}
