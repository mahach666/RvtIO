using System.Globalization;
using System.Text.Json;
using RvtIO.Compact;
using RvtIO.BuiltIns;
using RvtIO.Lookup;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The parameter, group and category labels of the compact JSON export (CompactLabels).</summary>
public class CompactLabelsTests
{
    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }

    private static IReadOnlyDictionary<int, string> GeneratedStrings() =>
        PeStringTable.Read(RevitInstallResources.Dll("en-US", "GeneratedRes.dll"), 1033)!;

    /// <summary>The label and group label of 362 builtin parameters as an independent export of the labels prints them, frozen as hashes.</summary>
    public static IEnumerable<object[]> LabelVectors() =>
        FrozenFixtures.Rows("label_vectors.tsv", 3).Select(row => new object[] { FrozenFixtures.Int(row[0]), row[1], row[2] });

    [Theory]
    [MemberData(nameof(LabelVectors))]
    public void ParameterLabelAndGroupEqualTheReferenceOutput(int id, string labelHash, string groupHash)
    {
        Assert.True(CompactLabels.TryGetParameterLabel(id, out string actualLabel));
        Assert.Equal(labelHash, FrozenFixtures.Sha256(actualLabel));
        Assert.True(CompactLabels.TryGetParameterGroupLabel(id, out string actualGroup));
        Assert.Equal(groupHash, FrozenFixtures.Sha256(actualGroup));
    }

    [Fact]
    public void LabelVectorsAreNotEmpty() => Assert.Equal(362, LabelVectors().Count());

    /// <summary>The embedded table is exactly what the original Revit 2022 definition table and the original en-US
    /// GeneratedRes strings say: no row is invented, none is dropped but those without a plain caption.</summary>
    [SkippableFact]
    public void ParameterTableEqualsTheRevit2022Sources()
    {
        if (RevitInstallResources.Root == null) throw new SkipException(RevitInstallResources.SkipReason!);
        string source = FrozenFixtureOrigin.SourceFile()
            ?? throw new SkipException($"Set {FrozenFixtureOrigin.SourceVariable} to the folder with {FrozenFixtureBuilder.SourceFileName}.");
        var strings = GeneratedStrings();
        using var json = JsonDocument.Parse(File.ReadAllBytes(source));
        var columns = json.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();
        int idColumn = columns.IndexOf("id"), groupColumn = columns.IndexOf("groupId"), resourceColumn = columns.IndexOf("originalResourceId");
        int listed = 0, notListed = 0;
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            int id = row[idColumn].GetInt32(), resource = row[resourceColumn].GetInt32();
            if (resource == -1 || strings[resource].IndexOfAny(['\t', '\r', '\n']) >= 0 || strings[resource] != strings[resource].Trim())
            {
                Assert.False(CompactLabels.TryGetParameterLabel(id, out _), id.ToString());
                notListed++;
                continue;
            }
            Assert.True(CompactLabels.TryGetParameterLabel(id, out string label), id.ToString());
            Assert.Equal(strings[resource], label);
            Assert.True(CompactLabels.TryGetParameterGroupId(id, out int group), id.ToString());
            Assert.Equal(row[groupColumn].GetInt32(), group);
            listed++;
        }
        Assert.Equal(3342, listed);
        Assert.Equal(10, notListed);
    }

    /// <summary>The Definition.Name and ParameterGroup captured from a genuine Revit 2022 are what the table
    /// says, for all 2050 captured parameters (but one with a blank at the end of its name): the GeneratedRes route reproduces the public API.</summary>
    [Fact]
    public void ParameterTableEqualsTheGenuineRevit2022ApiCapture()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(RepoFile("src", "RvtIO", "Resources", "api_parameter_definitions_2022.json")));
        int checkedIds = 0, notListed = 0;
        foreach (var entry in json.RootElement.EnumerateObject())
        {
            int id = int.Parse(entry.Name, CultureInfo.InvariantCulture);
            string name = entry.Value[0].GetString()!;
            if (name != name.Trim())
            {
                Assert.False(CompactLabels.TryGetParameterLabel(id, out _), entry.Name); // not a plain label: not listed
                notListed++;
                continue;
            }
            Assert.True(CompactLabels.TryGetParameterLabel(id, out string label), entry.Name);
            Assert.Equal(name, label);
            Assert.True(CompactLabels.TryGetParameterGroupId(id, out int group), entry.Name);
            Assert.Equal(entry.Value[1].GetInt32(), group);
            checkedIds++;
        }
        Assert.Equal(2049, checkedIds);
        Assert.Equal(1, notListed);
    }

    /// <summary>The identity block every node starts with (Type Name, Design Option, Workset, Edited by, IfcGUID) and the
    /// parameters whose definition has no group.</summary>
    [Theory]
    [InlineData(-1002001, "Type Name", "Identity Data")]
    [InlineData(-1013201, "Design Option", "Identity Data")]
    [InlineData(-1002053, "Workset", "Identity Data")]
    [InlineData(-1002067, "Edited by", "Identity Data")]
    [InlineData(-1019000, "IfcGUID", "IFC Parameters")]
    [InlineData(-1140362, "Category", "Invalid group")]
    [InlineData(-1140363, "Category", "Invalid group")]
    [InlineData(-1002051, "Family", "Invalid group")]
    [InlineData(-1002052, "Family and Type", "Invalid group")]
    [InlineData(-1002050, "Type", "Invalid group")]
    [InlineData(-1002000, "Type Id", "Invalid group")]
    [InlineData(-1002002, "Family Name", "Invalid group")]
    [InlineData(-1002108, "Host Id", "Invalid group")]
    public void IdentityBlockParameters(int id, string label, string group)
    {
        Assert.True(CompactLabels.TryGetParameterLabel(id, out string actualLabel));
        Assert.Equal(label, actualLabel);
        Assert.True(CompactLabels.TryGetParameterGroupLabel(id, out string actualGroup));
        Assert.Equal(group, actualGroup);
    }

    /// <summary>Every group label is a ParamGroup string of the original GeneratedRes, and agrees with the labels the
    /// repository already holds for the Revit 2021 enum.</summary>
    [RevitInstallFact]
    public void GroupLabelsAreGeneratedResStringsAndAgreeWithTheEnumExtension()
    {
        var strings = GeneratedStrings();
        var groupStrings = File.ReadAllLines(RevitInstallResources.BundleFile("GeneratedResourceIds.inl"))
            .Where(line => line.StartsWith("#define String_Generated_ParamGroup_", StringComparison.Ordinal))
            .Select(line => strings[int.Parse(line[(line.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture)])
            .ToHashSet(StringComparer.Ordinal);
        int known = 0;
        for (int group = -5000300; group <= -5000001; group++)
        {
            if (!CompactLabels.TryGetGroupLabel(group, out string label)) continue;
            Assert.Contains(label, groupStrings);
            known++;
        }
        Assert.Equal(120, known);
        foreach (var group in Enum.GetValues<BuiltInParameterGroup>().Where(g => g != BuiltInParameterGroup.INVALID))
            if (group.ToEnglishLabel(out string enumLabel))
            {
                Assert.True(CompactLabels.TryGetGroupLabel((int)group, out string label), group.ToString());
                Assert.Equal(enumLabel, label);
            }
    }

    [Theory]
    [InlineData(-1, "Invalid group")]
    [InlineData(-5000100, "Identity Data")]
    [InlineData(-5000119, "Constraints")]
    [InlineData(-5000110, "Electrical Engineering")]
    [InlineData(-5000130, "Electrical")]
    [InlineData(-5000232, "Invalid group")] // a group of later versions: unknown here, the expected export's rule for an unknown group
    [InlineData(0, "Invalid group")]
    public void GroupLabelOfAGroupId(int group, string expected) => Assert.Equal(expected, CompactLabels.GroupLabel(group));

    [Fact]
    public void InvalidGroupOfAParameterIsLabelledInvalidGroup()
    {
        // Category (ELEM_CATEGORY_PARAM): its definition has no group.
        Assert.True(CompactLabels.TryGetParameterGroupId(-1140362, out int group));
        Assert.Equal(CompactLabels.InvalidGroupId, group);
        Assert.True(CompactLabels.TryGetParameterGroupLabel(-1140362, out string label));
        Assert.Equal("Invalid group", label);
    }

    [Theory]
    [InlineData(-1019012)] // Export to IFC: not in the Revit 2022 table
    [InlineData(-1016014)]
    [InlineData(-1006703)] // its Revit 2022 caption ends with a line break: not a plain label
    [InlineData(-1006012)] // its Revit 2022 caption ends with a blank: not a plain label
    [InlineData(0)]
    [InlineData(1234567)]
    public void AParameterNoSourceListsIsNotLabelled(int id)
    {
        Assert.False(CompactLabels.TryGetParameterLabel(id, out string label));
        Assert.Equal("", label);
        Assert.False(CompactLabels.TryGetParameterGroupId(id, out _));
        Assert.False(CompactLabels.TryGetParameterGroupLabel(id, out _));
    }

    [Theory]
    [InlineData(-2000011, "Walls")]
    [InlineData(-2000023, "Doors")]
    [InlineData(-2000032, "Floors")]
    public void CategoryLabelOfABuiltinCategory(int id, string expected)
    {
        Assert.True(CompactLabels.TryGetCategoryLabel(id, out string label));
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(288495)]
    public void ACategoryOfTheFileHasNoBuiltinLabel(int id) => Assert.False(CompactLabels.TryGetCategoryLabel(id, out _));

    /// <summary>The embedded table is exactly what the frozen table of the original Revit 2022 definitions and the frozen hashes of the
    /// original en-US GeneratedRes strings say: no row is invented, none is dropped but those without a plain caption. Runs without
    /// the original files; ParameterTableEqualsTheRevit2022Sources is the same check against them.</summary>
    [Fact]
    public void ParameterTableEqualsTheFrozenRevit2022Table()
    {
        var rows = FrozenFixtures.Rows("parameter_table.tsv", 5);
        int listed = 0, notListed = 0;
        foreach (var row in rows)
        {
            int id = FrozenFixtures.Int(row[0]);
            if (row[3] != "1")
            {
                Assert.False(CompactLabels.TryGetParameterLabel(id, out _), row[0]);
                notListed++;
                continue;
            }
            Assert.True(CompactLabels.TryGetParameterLabel(id, out string label), row[0]);
            Assert.Equal(row[4], FrozenFixtures.Sha256(label));
            Assert.True(CompactLabels.TryGetParameterGroupId(id, out int group), row[0]);
            Assert.Equal(FrozenFixtures.Int(row[1]), group);
            listed++;
        }
        Assert.Equal(3352, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r[0]).Distinct().Count());
        Assert.Equal(3342, listed);
        Assert.Equal(10, notListed);
        Assert.Equal(listed, FrozenFixtures.EmbeddedParameters().Count);
    }

    /// <summary>Every group label is a ParamGroup string of the original GeneratedRes (frozen as hashes), and agrees with the labels the
    /// repository already holds for the Revit 2021 enum. Runs without the original files; GroupLabelsAreGeneratedResStringsAndAgreeWithTheEnumExtension is the same check against them.</summary>
    [Fact]
    public void GroupLabelsAreFrozenGeneratedResStringsAndAgreeWithTheEnumExtension()
    {
        var groupRows = FrozenFixtures.Rows("group_labels.tsv", 3);
        Assert.Equal(120, groupRows.Count);
        var groupHashes = groupRows.Select(r => r[2]).ToHashSet(StringComparer.Ordinal);
        int known = 0;
        for (int group = -5000300; group <= -5000001; group++)
        {
            if (!CompactLabels.TryGetGroupLabel(group, out string label)) continue;
            Assert.Contains(FrozenFixtures.Sha256(label), groupHashes);
            known++;
        }
        Assert.Equal(120, known);
        foreach (var group in Enum.GetValues<BuiltInParameterGroup>().Where(g => g != BuiltInParameterGroup.INVALID))
            if (group.ToEnglishLabel(out string enumLabel))
            {
                Assert.True(CompactLabels.TryGetGroupLabel((int)group, out string label), group.ToString());
                Assert.Equal(enumLabel, label);
            }
    }

    /// <summary>With the original files at hand: the committed frozen fixtures are what the originals give. The fixtures of the
    /// parameter table and the label vectors also need the private resource table and vectors (see docs/frozen-resource-fixtures.md).</summary>
    [SkippableFact]
    public void FrozenFixturesEqualWhatTheOriginalResourcesGive()
    {
        string root = RevitInstallResources.Root ?? throw new SkipException(RevitInstallResources.SkipReason!);
        var built = FrozenFixtureBuilder.Build(root, FrozenFixtureOrigin.SourceFile(), FrozenFixtureOrigin.LabelVectorsFile());
        foreach (var (name, text) in built)
            Assert.Equal(FrozenFixtures.Text(name), text);
        Assert.Contains(FrozenFixtureBuilder.UiNames, built.Keys);
    }

    [SkippableFact]
    public void FrozenParameterTableEqualsTheNativeDefinitionTable()
    {
        string root = RevitInstallResources.Root ?? throw new SkipException(RevitInstallResources.SkipReason!);
        string source = FrozenFixtureOrigin.SourceFile()
            ?? throw new SkipException($"Set {FrozenFixtureOrigin.SourceVariable} to the folder with {FrozenFixtureBuilder.SourceFileName}.");
        Assert.Equal(FrozenFixtures.Text(FrozenFixtureBuilder.ParameterTable),
            FrozenFixtureBuilder.Build(root, source, null)[FrozenFixtureBuilder.ParameterTable]);
    }

    [SkippableFact]
    public void FrozenLabelVectorsEqualThePrivateVectors()
    {
        string root = RevitInstallResources.Root ?? throw new SkipException(RevitInstallResources.SkipReason!);
        string vectors = FrozenFixtureOrigin.LabelVectorsFile() ?? throw TestFiles.Missing("TestData/compact_label_vectors.tsv");
        Assert.Equal(FrozenFixtures.Text(FrozenFixtureBuilder.LabelVectors),
            FrozenFixtureBuilder.Build(root, null, vectors)[FrozenFixtureBuilder.LabelVectors]);
    }

    /// <summary>Writes the fixtures derived from the originals to the folder named by RVTIO_FROZEN_OUT (not a check; skipped otherwise).</summary>
    [SkippableFact]
    public void RegenerateFrozenFixtures()
    {
        string root = RevitInstallResources.Root ?? throw new SkipException(RevitInstallResources.SkipReason!);
        if (Environment.GetEnvironmentVariable("RVTIO_FROZEN_OUT") is not { Length: > 0 } output)
            throw new SkipException("Set RVTIO_FROZEN_OUT to a folder to write the frozen fixtures to.");
        Directory.CreateDirectory(output);
        foreach (var (name, text) in FrozenFixtureBuilder.Build(root, FrozenFixtureOrigin.SourceFile(), FrozenFixtureOrigin.LabelVectorsFile()))
            File.WriteAllText(Path.Combine(output, name), text, new System.Text.UTF8Encoding(false));
    }
}
