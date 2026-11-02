namespace RvtIO.Parsing;

/// <summary>
/// The class schema a Revit file carries in its own stream Formats/Latest, as the element-table
/// layout reads it (fields with an optional class reference and fixed count).
/// </summary>
internal sealed class FormatsSchema
{
    internal sealed record Field(string Name, byte Type, byte Mods, int? Ref, uint? Count);

    internal sealed class ClassDef
    {
        internal int Id { get; init; }
        internal string Name { get; init; } = "";
        internal int Base { get; set; }
        internal uint Version { get; set; }
        internal List<Field> Fields { get; } = [];
    }

    internal IReadOnlyDictionary<int, ClassDef> Classes => _classes;
    private readonly Dictionary<int, ClassDef> _classes = [];
    private readonly Dictionary<string, int> _byName = [];

    internal FormatsSchema(SchemaDocument document)
    {
        foreach (var c in document.Classes)
        {
            var def = new ClassDef { Id = c.Id, Name = c.Name, Base = c.Base, Version = (uint)c.Version };
            def.Fields.AddRange(c.Fields.Where(f => f.Stored).Select(f => new Field(f.Name, f.Type, f.Mods, f.Ref, (uint?)f.Fixed)));
            _classes[c.Id] = def;
            _byName.TryAdd(c.Name, c.Id);
        }
    }

    /// <summary>The release whose layouts apply to this file (<see cref="SchemaRelease.Of"/>); null for a schema parsed from bytes alone.</summary>
    internal int? Release { get; private set; }

    /// <summary>
    /// The id of the class with this exact name, or null (names are unique in every checked file).
    /// </summary>
    internal int? IdOf(string name) => _byName.TryGetValue(name, out int id) ? id : null;

    internal ClassDef? Get(string name) => IdOf(name) is int id ? _classes[id] : null;

    /// <summary>
    /// The schema of the file, or null when Formats/Latest is missing or does not decode exactly.
    /// </summary>
    internal static FormatsSchema? TryRead(OpenMcdf.RootStorage root)
        => TfSchemaReader.Read(root) is { } document ? new FormatsSchema(document).WithRelease(root) : null;

    private FormatsSchema WithRelease(OpenMcdf.RootStorage root)
    {
        Release = SchemaRelease.Of(root, SchemaRelease.FromSchema(this));
        return this;
    }

    /// <summary>Parse a decoded Formats/Latest stream; null unless every byte follows the grammar.</summary>
    internal static FormatsSchema? TryParse(byte[] data)
        => TfSchemaReader.TryParse(data) is { } document ? new FormatsSchema(document) : null;
}
