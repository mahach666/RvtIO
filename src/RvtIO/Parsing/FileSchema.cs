namespace RvtIO.Parsing;

/// <summary>
/// The class schema a Revit file carries about itself (stream Formats/Latest, decoded by <see
/// cref="PagedGzipStream"/>): for every serialised class its name, base class and field list.
/// </summary>
internal sealed class FileSchema
{
    /// <summary>
    /// One field. <see cref="Ref"/> is the class id of a class-typed field (type 0x0E), else 0.
    /// </summary>
    internal sealed record Field(string Name, byte Type, byte Mods, int Ref);

    internal sealed record Class(int Id, string Name, int Base, IReadOnlyList<Field> Fields);

    private readonly Dictionary<int, Class> _classes = [];
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    internal FileSchema(SchemaDocument document)
    {
        Document = document;
        foreach (var c in document.Classes)
        {
            _classes[c.Id] = new Class(c.Id, c.Name, c.Base, [.. c.Fields.Where(f => f.Stored).Select(f => new Field(f.Name, f.Type, f.Mods, f.Ref ?? 0))]);
            _byName.TryAdd(c.Name, c.Id);
        }
    }

    /// <summary>The parsed document this view is built on (the one model of the file's schema).</summary>
    internal SchemaDocument Document { get; }

    internal IReadOnlyDictionary<int, Class> Classes => _classes;

    internal Class? Get(int id) => _classes.GetValueOrDefault(id);

    /// <summary>The class of this name; the first one when the table declares a name twice.</summary>
    internal Class? ByName(string name) => _byName.TryGetValue(name, out int id) ? _classes[id] : null;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _chains = new(StringComparer.Ordinal);

    /// <summary>
    /// The class of this name followed by its base classes (empty when the table has no such
    /// class).
    /// </summary>
    internal IReadOnlyList<string> ChainOf(string name)
    {
        return _chains.GetOrAdd(name, n =>
        {
            var chain = new List<string>();
            for (var c = ByName(n); c != null && chain.Count < 64; c = c.Base == 0 ? null : Get(c.Base)) chain.Add(c.Name);
            return [.. chain];
        });
    }

    /// <summary>True when <paramref name="id"/> is <paramref name="ancestor"/> or derives from it.</summary>
    internal bool IsSubclassOf(int id, int ancestor)
    {
        for (int guard = 0; id != 0 && guard < 64; guard++)
        {
            if (id == ancestor) return true;
            if (!_classes.TryGetValue(id, out var c)) return false;
            id = c.Base;
        }
        return false;
    }

    /// <summary>
    /// Serialised byte size of a class whose every field is a fixed scalar or a fixed class, or
    /// null when it has a container, pointer, string or a scalar type of unknown width.
    /// </summary>
    internal int? FixedSize(int classId)
    {
        if (!_classes.TryGetValue(classId, out var c)) return null;
        int total = 0;
        foreach (var f in c.Fields)
        {
            int? size = FieldSize(f);
            if (size == null) return null;
            total += size.Value;
        }
        return total;
    }

    internal int? FieldSize(Field f)
    {
        if ((f.Mods >> 4) != 0) return null;
        return f.Type switch
        {
            3 => 2,
            4 => 4,
            9 => 16,
            11 => 8,
            0x0E when (f.Mods & 0x0F) == 0 && f.Ref != 0 => FixedSize(f.Ref),
            // a pointer field (low mods nibble 1..3) is run-time state: the Revit 2027 ElemRec
            // declares three of them in front of its fields and its rows are still the 40 bytes of
            // the Revit 2025 record that has none
            0x0E when (f.Mods & 0x0F) != 0 => 0,
            _ => null,
        };
    }

    /// <summary>The fields of a class that are serialised: all but the run-time pointers.</summary>
    internal static IReadOnlyList<Field> SerializedFields(Class c) =>
        c.Fields.Where(f => !(f.Type == 0x0E && (f.Mods >> 4) == 0 && (f.Mods & 0x0F) != 0)).ToList();

    /// <summary>
    /// Byte offset of field <paramref name="fieldName"/> inside a fixed-size class, or null.
    /// </summary>
    internal int? OffsetOf(int classId, string fieldName)
    {
        if (!_classes.TryGetValue(classId, out var c)) return null;
        int offset = 0;
        foreach (var f in c.Fields)
        {
            if (f.Name == fieldName) return offset;
            int? size = FieldSize(f);
            if (size == null) return null;
            offset += size.Value;
        }
        return null;
    }

    /// <summary>The schema of the file, or null when Formats/Latest is missing or does not parse.</summary>
    internal static FileSchema? Read(OpenMcdf.RootStorage root)
    {
        if (TfSchemaReader.Read(root) is not { } document) return null;
        var schema = new FileSchema(document);
        schema.Release = SchemaRelease.Of(root, SchemaRelease.FromSchema(schema));
        return schema;
    }

    /// <summary>The release whose layouts apply to this file (<see cref="SchemaRelease.Of"/>); null for a schema parsed from bytes alone.</summary>
    internal int? Release { get; private set; }

    /// <summary>Parses a decoded Formats/Latest stream; null on any grammar violation.</summary>
    internal static FileSchema? Parse(byte[] data) =>
        TfSchemaReader.TryParse(data) is { } document ? new FileSchema(document) : null;
}
