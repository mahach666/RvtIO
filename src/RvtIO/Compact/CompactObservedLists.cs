using System.Globalization;
using RvtIO.Parsing;

namespace RvtIO.Compact;

internal static class CompactObservedLists
{
    private sealed record ClassTable(string[] Features, Dictionary<string, int[]> Rows);

    private static readonly Lazy<Dictionary<string, ClassTable>> Tables = new(Parse);

    internal static bool IsSupported(string recordClass) => Tables.Value.ContainsKey(recordClass);

    internal static CompactListResult GetBuiltinIds(string recordClass, ICompactListFacts facts)
    {
        if (!Tables.Value.TryGetValue(recordClass, out var table)) return new CompactListResult(null, "class " + recordClass + " not covered");
        // every record has a cell list tag; without it the record was not decoded and an absent field means nothing
        if (table.Features.Any(feature => feature != "hdr") && facts.Get("cellList") is null)
            return new CompactListResult(null, "record of the element not decoded");
        var values = new string[table.Features.Length];
        for (int i = 0; i < values.Length; i++)
        {
            string? value = Value(table.Features[i], facts);
            if (value is null) return new CompactListResult(null, "feature " + table.Features[i]);
            values[i] = value;
        }
        return table.Rows.TryGetValue(string.Join('|', values), out int[]? ids)
            ? new CompactListResult(ids, null)
            : new CompactListResult(null, "combination of " + string.Join(',', table.Features) + " not observed");
    }

    private static string? Value(string feature, ICompactListFacts facts)
    {
        if (feature == "hdr") return Text(facts.Get("headerCategory"));
        if (feature.StartsWith("ptr:", StringComparison.Ordinal))
        {
            if (facts.Get("field:" + feature[4..]) is not int tag) return "-";
            return tag == 0 ? "null" : TfBlockDeserializer.Schemas.TryGetValue(tag, out var schema) ? schema.Name : null;
        }
        if (feature.StartsWith("f:", StringComparison.Ordinal)) return facts.Get("field:" + feature[2..]) is int number ? Text(number) : "-";
        if (feature.StartsWith("cell:", StringComparison.Ordinal)) return Text(facts.Get("cellHas:" + feature[5..]));
        if (feature.StartsWith("stored:", StringComparison.Ordinal)) return Text(facts.Get(feature));
        return null;
    }

    private static string? Text(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static Dictionary<string, ClassTable> Parse()
    {
        using var stream = typeof(CompactObservedLists).Assembly.GetManifestResourceStream("RvtIO.Resources.compact_observed_lists.tsv")
            ?? throw new InvalidOperationException("Missing resource compact_observed_lists.tsv.");
        using var reader = new StreamReader(stream);
        var features = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var rows = new Dictionary<string, Dictionary<string, int[]>>(StringComparer.Ordinal);
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            switch (fields[0])
            {
                case "F":
                    features[fields[1]] = [.. fields[2..].Where(feature => feature.Length > 0)];
                    rows[fields[1]] = new Dictionary<string, int[]>(StringComparer.Ordinal);
                    break;
                case "R":
                    rows[fields[1]].Add(fields[2], fields[3].Length == 0 ? [] : [.. fields[3].Split(',').Select(id => int.Parse(id, CultureInfo.InvariantCulture))]);
                    break;
                default: throw new InvalidDataException("Malformed line in compact_observed_lists.tsv: " + line);
            }
        }
        return features.ToDictionary(pair => pair.Key, pair => new ClassTable(pair.Value, rows[pair.Key]), StringComparer.Ordinal);
    }
}
