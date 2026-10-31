using System.Security.Cryptography;
using System.Text;

namespace RvtIO.Tests;

/// <summary>The frozen fixtures (Fixtures/Frozen/*.tsv, embedded in the test assembly): identifiers and SHA-256 hashes of strings
/// of the original Revit 2022 resources, derived once from the originals (see docs/frozen-resource-fixtures.md). They let the tests
/// check the embedded data tables without the original files and without any of their text.</summary>
internal static class FrozenFixtures
{
    public const string Prefix = "Frozen.";

    public static string Text(string name)
    {
        using var stream = typeof(FrozenFixtures).Assembly.GetManifestResourceStream(Prefix + name)
            ?? throw new InvalidOperationException($"Missing fixture {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        // A checkout may have turned the line breaks into CR LF.
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>The data rows of a fixture; each must have exactly <paramref name="columns"/> tab separated fields.</summary>
    public static List<string[]> Rows(string name, int columns)
    {
        var rows = new List<string[]>();
        foreach (string line in Text(name).Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            if (fields.Length != columns) throw new InvalidDataException($"Malformed line in {name}: {line}");
            rows.Add(fields);
        }
        return rows;
    }

    /// <summary>The rows of the embedded table of builtin parameter labels (compact_builtin_parameters.tsv): id, group id, label.</summary>
    public static List<(int Id, int Group, string Label)> EmbeddedParameters()
    {
        using var stream = typeof(RvtIO.Compact.CompactLabels).Assembly.GetManifestResourceStream("RvtIO.Resources.compact_builtin_parameters.tsv")
            ?? throw new InvalidOperationException("Missing embedded compact_builtin_parameters.tsv.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var rows = new List<(int, int, string)>();
        for (string? line; (line = reader.ReadLine()) != null;)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            rows.Add((Int(fields[0]), Int(fields[1]), fields[2]));
        }
        return rows;
    }

    public static int Int(string field) => int.Parse(field, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Lower-case hexadecimal SHA-256 of the UTF-8 bytes of a string.</summary>
    public static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>The comparison key the IFC exporter of Revit uses for a parameter caption: spaces removed, case ignored.</summary>
    public static string Key(string caption) => caption.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
