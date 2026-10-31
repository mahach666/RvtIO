using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RvtIO.Lookup;

namespace RvtIO.Tests;

/// <summary>Derives the frozen fixtures (Fixtures/Frozen/*.tsv) from the original Revit 2022 resources. It is used only by the tests
/// that need RVTIO_REVIT_RESOURCES: they compare what it produces with the committed fixtures, and
/// <c>RegenerateFrozenFixtures</c> writes the files out. See docs/frozen-resource-fixtures.md.</summary>
internal static class FrozenFixtureBuilder
{
    public const string ParameterTable = "parameter_table.tsv";
    public const string CaptionKeys = "caption_keys.tsv";
    public const string OwnFamilyCaptions = "own_family_captions.tsv";
    public const string GroupLabels = "group_labels.tsv";
    public const string UiNames = "ui_names.tsv";
    public const string LabelVectors = "label_vectors.tsv";
    public const string OriginalPins = "original_pins.tsv";

    public const string SourceFileName = "177-native-builtin-definition-table.json";

    /// <summary>The symbols of the parameters of the electrical panel family whose captions the Revit initializer names.</summary>
    public static readonly string[] OwnFamilySymbols =
    [
        "RBS_ELEC_PANEL_NAME", "RBS_ELEC_PANEL_MCB_RATING_PARAM", "RBS_ELEC_PANEL_SUBFEED_LUGS_PARAM",
        "RBS_ELEC_PANEL_FEED_THRU_LUGS_PARAM", "RBS_ELEC_MOUNTING", "RBS_ELEC_ENCLOSURE",
    ];

    private static readonly (string Culture, ushort Language)[] Cultures = [("en-US", 1033), ("ru-RU", 1049)];
    private const string ParamPrefix = "#define String_Generated_Param_";
    private const string GroupPrefix = "#define String_Generated_ParamGroup_";

    private static string Header(string description, string columns) =>
        "# " + description + "\n# Frozen from the original Revit 2022 resources: identifiers and SHA-256 hashes only (docs/frozen-resource-fixtures.md).\n# Columns (tab separated): " + columns + "\n";

    private static string Sha(string text) => FrozenFixtures.Sha256(text);

    private static IReadOnlyDictionary<int, string> Strings(string root, string culture, ushort language, string dll) =>
        PeStringTable.Read(File.ReadAllBytes(Path.Combine(root, "2022", culture, dll)), language)
        ?? throw new InvalidDataException($"{culture}/{dll} has no string table.");

    private static List<(string Symbol, int Id)> Defines(string root, string prefix) =>
        File.ReadAllLines(Path.Combine(root, "2022", "GeneratedResourceIds.inl"))
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => (line[prefix.Length..line.LastIndexOf(' ')], int.Parse(line[(line.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture)))
            .ToList();

    /// <summary>All fixtures that can be derived from what is given: the resource root always; the table of the native parameter
    /// definitions (a separate table file) and the private label vectors when present.</summary>
    public static IReadOnlyDictionary<string, string> Build(string root, string? sourceFile, string? labelVectorsFile)
    {
        var result = new Dictionary<string, string>
        {
            [OriginalPins] = Pins(root),
            [CaptionKeys] = Keys(root),
            [OwnFamilyCaptions] = OwnFamily(root),
            [GroupLabels] = Groups(root),
            [UiNames] = Ui(root),
        };
        if (sourceFile != null) result[ParameterTable] = Parameters(root, sourceFile);
        if (labelVectorsFile != null) result[LabelVectors] = Vectors(labelVectorsFile);
        return result;
    }

    private static string Pins(string root)
    {
        var text = new StringBuilder(Header("Length, SHA-256 and line count of the original files the other fixtures were derived from.",
            "file relative to the resource root, length in bytes, SHA-256 of the bytes, number of lines (- for a binary file)"));
        var files = new List<string> { "2022/GeneratedResourceIds.inl" };
        foreach (var (culture, _) in Cultures)
            foreach (string path in Directory.GetFiles(Path.Combine(root, "2022", culture), "*.dll"))
                files.Add($"2022/{culture}/{Path.GetFileName(path)}");
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, file));
            string lines = file.EndsWith(".inl", StringComparison.Ordinal) ? File.ReadAllLines(Path.Combine(root, file)).Length.ToString(CultureInfo.InvariantCulture) : "-";
            text.Append(file).Append('\t').Append(bytes.Length).Append('\t').Append(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()).Append('\t').Append(lines).Append('\n');
        }
        return text.ToString();
    }

    private static string Keys(string root)
    {
        var english = Strings(root, "en-US", 1033, "GeneratedRes.dll");
        var russian = Strings(root, "ru-RU", 1049, "GeneratedRes.dll");
        var text = new StringBuilder(Header("The 3351 builtin parameter captions of the original resources, as the comparison key of the IFC parameter name cache (spaces removed, upper case).",
            "symbol, string id, SHA-256 of the English key, SHA-256 of the Russian key"));
        foreach (var (symbol, id) in Defines(root, ParamPrefix))
        {
            string[] keys = [FrozenFixtures.Key(english[id]), FrozenFixtures.Key(russian[id])];
            foreach (string caption in new[] { english[id], russian[id] })
            {
                if (string.IsNullOrWhiteSpace(caption) || caption.Contains('\0')) throw new InvalidDataException($"Unusable caption of {symbol}.");
                string key = caption.Replace(" ", string.Empty, StringComparison.Ordinal);
                if (StringComparer.InvariantCultureIgnoreCase.Equals(key, "IfcName") || StringComparer.InvariantCultureIgnoreCase.Equals(key, "NameOverride"))
                    throw new InvalidDataException($"{symbol} is named like an IFC name override.");
            }
            text.Append(symbol).Append('\t').Append(id).Append('\t').Append(Sha(keys[0])).Append('\t').Append(Sha(keys[1])).Append('\n');
        }
        return text.ToString();
    }

    private static string OwnFamily(string root)
    {
        var ids = Defines(root, ParamPrefix).ToDictionary(pair => pair.Symbol, pair => pair.Id);
        var text = new StringBuilder(Header("SHA-256 of the exact captions of the six parameters of the electrical panel family, in both languages.",
            "culture, symbol, string id, SHA-256 of the caption"));
        foreach (var (culture, language) in Cultures)
        {
            var strings = Strings(root, culture, language, "GeneratedRes.dll");
            foreach (string symbol in OwnFamilySymbols)
                text.Append(culture).Append('\t').Append(symbol).Append('\t').Append(ids[symbol]).Append('\t').Append(Sha(strings[ids[symbol]])).Append('\n');
        }
        return text.ToString();
    }

    private static string Groups(string root)
    {
        var english = Strings(root, "en-US", 1033, "GeneratedRes.dll");
        var text = new StringBuilder(Header("The parameter group labels (ParamGroup strings) of the original English resources.",
            "symbol, string id, SHA-256 of the label"));
        foreach (var (symbol, id) in Defines(root, GroupPrefix))
            text.Append(symbol).Append('\t').Append(id).Append('\t').Append(Sha(english[id])).Append('\n');
        return text.ToString();
    }

    private static string Ui(string root)
    {
        var text = new StringBuilder(Header("The user interface strings the reader can request, as the original resource files of both languages hold them.",
            "culture, resource file, string id, SHA-256 of the text"));
        foreach (var (culture, language) in Cultures)
            foreach (var group in RevitSystemFamilyNames.RequestableStrings.GroupBy(pair => pair.Dll).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var strings = Strings(root, culture, language, group.Key);
                foreach (int id in group.Select(pair => pair.Id).Order())
                    text.Append(culture).Append('\t').Append(group.Key).Append('\t').Append(id).Append('\t').Append(Sha(strings[id])).Append('\n');
            }
        return text.ToString();
    }

    private static string Parameters(string root, string sourceFile)
    {
        var strings = Strings(root, "en-US", 1033, "GeneratedRes.dll");
        using var json = JsonDocument.Parse(File.ReadAllBytes(sourceFile));
        var columns = json.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();
        int idColumn = columns.IndexOf("id"), groupColumn = columns.IndexOf("groupId"), resourceColumn = columns.IndexOf("originalResourceId");
        var text = new StringBuilder(Header("The builtin parameter definitions of the original Revit 2022: parameter id, group, English caption (as a hash).",
            "builtin parameter id, group id, string id (-1 none), 1 when the caption is a plain label (no tab or line break, no blank at either end) else 0, SHA-256 of the caption (- none)"));
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            int id = row[idColumn].GetInt32(), group = row[groupColumn].GetInt32(), resource = row[resourceColumn].GetInt32();
            bool plain = resource != -1 && strings[resource].IndexOfAny(['\t', '\r', '\n']) < 0 && strings[resource] == strings[resource].Trim();
            text.Append(id).Append('\t').Append(group).Append('\t').Append(resource).Append('\t').Append(plain ? 1 : 0).Append('\t')
                .Append(resource == -1 ? "-" : Sha(strings[resource])).Append('\n');
        }
        return text.ToString();
    }

    private static string Vectors(string file)
    {
        var text = new StringBuilder(Header("Builtin parameter label and group label vectors checked against an independent export of the labels.",
            "builtin parameter id, SHA-256 of the label, SHA-256 of the group label"));
        foreach (string line in File.ReadAllLines(file))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] fields = line.Split('\t');
            text.Append(fields[0]).Append('\t').Append(Sha(fields[1])).Append('\t').Append(Sha(fields[2])).Append('\n');
        }
        return text.ToString();
    }
}

/// <summary>Where the optional private inputs of the fixture derivation are looked for.</summary>
internal static class FrozenFixtureOrigin
{
    public const string SourceVariable = "RVTIO_NATIVE_TABLE_DIR";

    /// <summary>The table of the native builtin parameter definitions, in the folder named by RVTIO_NATIVE_TABLE_DIR, or null.</summary>
    public static string? SourceFile() =>
        Environment.GetEnvironmentVariable(SourceVariable) is { Length: > 0 } folder
            && Path.Combine(folder, FrozenFixtureBuilder.SourceFileName) is var file && File.Exists(file) ? file : null;

    /// <summary>The private label vectors (builtin parameter id, label, group label) among the test files, or null.</summary>
    public static string? LabelVectorsFile() => TestFiles.TryFindRelative(Path.Combine("TestData", "compact_label_vectors.tsv"));
}
