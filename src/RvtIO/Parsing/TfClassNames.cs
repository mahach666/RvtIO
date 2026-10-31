using System.Collections.Concurrent;

namespace RvtIO.Parsing;

/// <summary>
/// Class names against the numbering of the shipped Revit 2022 schema table (<see cref="TfBlockDeserializer.Schemas"/>).
/// Names are unique in that table, so a name identifies one 2022 id; an unknown name is a programming error and throws.
/// Class ids differ between Revit releases and names mostly do not, so decoders and rules name classes and let this table
/// (2022) or the file's own schema (<see cref="FileSchema"/>) supply the number.
/// </summary>
internal static class TfClassNames
{
    private static readonly Lazy<Dictionary<string, int>> Ids = new(() =>
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, schema) in TfBlockDeserializer.Schemas) map.Add(schema.Name, id);
        return map;
    });

    private static readonly ConcurrentDictionary<(string, string), string> Keys = new();

    /// <summary>The 2022 class id of this name; throws when the table has no such class.</summary>
    internal static int IdOf(string className)
        => Ids.Value.TryGetValue(className, out int id) ? id : throw new KeyNotFoundException($"no Revit 2022 class named {className}");

    /// <summary>The name of a 2022 class id, or null when the table has none.</summary>
    internal static string? NameOf(int classId)
        => TfBlockDeserializer.Schemas.TryGetValue(classId, out var schema) ? schema.Name : null;

    /// <summary>The key of a field capture in <see cref="Model.Element.NativeListFields"/> and the other "class id:field" maps:
    /// the 2022 id of the class that declares the field, a colon and the field spelling (<c>m_familyId</c>, <c>m_refs#</c>, ...).</summary>
    internal static string Key(string declaringClass, string field)
        => Keys.GetOrAdd((declaringClass, field), static k => $"{IdOf(k.Item1)}:{k.Item2}");

    /// <summary>True when the class named <paramref name="className"/> is <paramref name="ancestor"/> or derives from it (2022 table).</summary>
    internal static bool IsSubclassOf(string className, string ancestor)
        => Ids.Value.TryGetValue(className, out int id) && TfBlockDeserializer.IsSubclassOf(id, IdOf(ancestor));
}
