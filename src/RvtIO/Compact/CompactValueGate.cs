using System.Globalization;

namespace RvtIO.Compact;

/// <summary>Which parameters' values the reader gives exactly.</summary>
internal sealed class CompactValueGate
{
    internal enum Source { None, Stored, Derived }

    private readonly HashSet<int> _stored = [];
    private readonly HashSet<int> _derived = [];
    private readonly HashSet<(string Class, int Id)> _derivedPairs = [];
    private readonly HashSet<string> _ownClasses = [];
    private readonly HashSet<(string Class, int Id)> _storedPairs = [];
    private readonly HashSet<(string Class, int Id)> _absentIsStored = [];
    private readonly HashSet<(string Class, int Id)> _excludedPairs = [];
    private readonly Dictionary<(string Class, int Id, int Category, string Shape), string> _solidRules = [];
    private readonly HashSet<int> _solidCategories = [];

    private CompactValueGate() { }

    private static readonly Lazy<CompactValueGate> Embedded = new(() => Parse(ReadResource()));
    private static readonly Lazy<CompactValueGate> EmbeddedWide = new(() => Parse(ReadResource(), exclusions: false));

    /// <summary>The gate shipped with the library.</summary>
    internal static CompactValueGate Default => Embedded.Value;

    internal static CompactValueGate ForLayout(bool wide) => wide ? EmbeddedWide.Value : Embedded.Value;

    internal Source Of(string recordClass, int id) =>
        _excludedPairs.Contains((recordClass, id)) ? Source.None
        : _ownClasses.Contains(recordClass) ? _storedPairs.Contains((recordClass, id)) ? Source.Stored : _derivedPairs.Contains((recordClass, id)) ? Source.Derived : Source.None
        : _stored.Contains(id) ? Source.Stored
        : _derived.Contains(id) || _derivedPairs.Contains((recordClass, id)) ? Source.Derived
        : Source.None;

    internal bool AbsentIsStored(string recordClass, int id) => _absentIsStored.Contains((recordClass, id));

    internal string? SolidRule(string recordClass, int id, int category, string shape)
    {
        foreach (string key in shape switch { "one" or "many" => new[] { shape, "solid", "any" }, "none" => new[] { "none", "any" }, _ => new[] { "any" } })
            if (_solidRules.TryGetValue((recordClass, id, category, key), out string? formula)) return formula;
        return null;
    }

    /// <summary>True when a <c>V</c> line is given for the header category.</summary>
    internal bool HasSolidRules(int category) => _solidCategories.Contains(category);

    internal int StoredCount => _stored.Count;

    internal int DerivedCount => _derived.Count;

    internal int DerivedPairCount => _derivedPairs.Count;

    internal int ExcludedPairCount => _excludedPairs.Count;

    internal static CompactValueGate Parse(string text, bool exclusions = true)
    {
        var gate = new CompactValueGate();
        foreach (string line in text.Split('\n'))
        {
            string row = line.TrimEnd('\r');
            if (row.Length == 0 || row[0] == '#') continue;
            string[] fields = row.Split('\t');
            if (fields[0] == "O")
            {
                if (fields.Length < 2) throw new InvalidDataException("Malformed line in compact_value_gate.tsv: " + row);
                gate._ownClasses.Add(fields[1]);
                continue;
            }
            bool pair = fields[0] is "C" or "Q" or "E" or "X" or "V";
            if (fields.Length < (fields[0] == "V" ? 6 : pair ? 3 : 2)
                || !int.TryParse(fields[pair ? 2 : 1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id))
                throw new InvalidDataException("Malformed line in compact_value_gate.tsv: " + row);
            switch (fields[0])
            {
                case "S": gate._stored.Add(id); break;
                case "D": gate._derived.Add(id); break;
                case "C": gate._derivedPairs.Add((fields[1], id)); break;
                case "Q": gate._storedPairs.Add((fields[1], id)); break;
                case "E": gate._absentIsStored.Add((fields[1], id)); break;
                case "X": if (exclusions) gate._excludedPairs.Add((fields[1], id)); break;
                case "V":
                    if (fields[4] is not ("none" or "one" or "many" or "solid" or "any") || fields[5] is not ("volume" or "half" or "maxface" or "prism" or "empty")
                        || !int.TryParse(fields[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int category))
                        throw new InvalidDataException("Malformed line in compact_value_gate.tsv: " + row);
                    gate._solidRules[(fields[1], id, category, fields[4])] = fields[5];
                    gate._solidCategories.Add(category);
                    break;
                default: throw new InvalidDataException("Unknown kind in compact_value_gate.tsv: " + row);
            }
        }
        if (gate._stored.Overlaps(gate._derived)) throw new InvalidDataException("compact_value_gate.tsv lists an id as both stored and derived.");
        return gate;
    }

    private static string ReadResource()
    {
        using var stream = typeof(CompactValueGate).Assembly.GetManifestResourceStream("RvtIO.Resources.compact_value_gate.tsv")
            ?? throw new InvalidOperationException("Missing resource compact_value_gate.tsv.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
