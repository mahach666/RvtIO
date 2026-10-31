using System.Security.Cryptography;
using RvtIO.Compact;
using RvtIO.BuiltIns;
using RvtIO.Lookup;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Original native caption resources. They are read from a Revit resource root
/// given by RVTIO_REVIT_RESOURCES (see <see cref="RevitInstallResources"/>); without it the tests that need it are skipped.</summary>
public class BuiltInParameterCaptionResourceTests
{
    private static string ResourceRoot() => Path.Combine(RevitInstallResources.Root!, "2022");

    private static byte[] Image(string culture) =>
        File.ReadAllBytes(Path.Combine(ResourceRoot(), culture, "GeneratedRes.dll"));

    [RevitInstallTheory]
    [InlineData("ru-RU", 1049, 8441, "Имя панели")]
    [InlineData("en-US", 1033, 8441, "Panel Name")]
    [InlineData("ru-RU", 1049, 8440, "Номинал MCB")]
    [InlineData("en-US", 1033, 8440, "MCB Rating")]
    [InlineData("ru-RU", 1049, 8448, "Активные наконечники")]
    [InlineData("en-US", 1033, 8448, "SubFeed Lugs")]
    [InlineData("ru-RU", 1049, 8436, "Питание через наконечники")]
    [InlineData("en-US", 1033, 8436, "Feed Through Lugs")]
    [InlineData("ru-RU", 1049, 8415, "Компоновка")]
    [InlineData("en-US", 1033, 8415, "Mounting")]
    [InlineData("ru-RU", 1049, 8403, "Корпус")]
    [InlineData("en-US", 1033, 8403, "Enclosure")]
    public void ReadsOriginalCaptionInRequestedLanguage(string culture, ushort language, int id, string expected)
    {
        var table = PeStringTable.Read(Image(culture), language);
        Assert.NotNull(table);
        Assert.Equal(expected, table[id]);
    }

    [RevitInstallTheory]
    [InlineData("ru-RU/GeneratedRes.dll", 195184, "36dcd286a1f4434d939eb79e598a8b6922e32d6f34f796971c471125646fd6ee")]
    [InlineData("en-US/GeneratedRes.dll", 157640, "39dd0b16c9b1e72c204e1efa207e230782de192b984371e89d77dd3fd2989ca7")]
    [InlineData("GeneratedResourceIds.inl", 269232, "1c14569f4e9a942a5919ff832cb31994c7f4ce45c2457f4cfee53644706c5234")]
    public void CopiesRetainOriginalBytes(string relative, int length, string expectedHash)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(ResourceRoot(), relative));
        Assert.Equal(length, bytes.Length);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [RevitInstallTheory]
    [InlineData("ru-RU", 1033)]
    [InlineData("en-US", 1049)]
    public void DoesNotSubstituteOtherLanguage(string culture, ushort language) =>
        Assert.Empty(PeStringTable.Read(Image(culture), language)!);

    [RevitInstallTheory]
    [InlineData("ru-RU", 1049)]
    [InlineData("en-US", 1033)]
    public void TruncatedResourceImageIsRejected(string culture, ushort language) =>
        Assert.Null(PeStringTable.Read(Image(culture)[..4096], language));

    [Theory]
    [InlineData("ru-RU", 1049, 1033)]
    [InlineData("en-US", 1033, 1049)]
    public void SyntheticImageDoesNotSubstituteOtherLanguage(string culture, ushort language, ushort other) =>
        Assert.Empty(PeStringTable.Read(SyntheticPe.Build(language, RevitUiNameTable.Get(culture, "Revit.dll")!), other)!);

    [Theory]
    [InlineData("ru-RU", 1049)]
    [InlineData("en-US", 1033)]
    public void SyntheticTruncatedImageIsRejected(string culture, ushort language) =>
        Assert.Null(PeStringTable.Read(SyntheticPe.Build(language, RevitUiNameTable.Get(culture, "Revit.dll")!)[..0x200], language));

    [RevitInstallTheory]
    [InlineData("ru-RU", 1049)]
    [InlineData("en-US", 1033)]
    public void EnumIdentifierIsNotNativeDefinitionCaption(string culture, ushort language)
    {
        Assert.Equal("RBS_ELEC_PANEL_NAME", BipNames.Lookup[-1140078]);
        Assert.NotEqual(BipNames.Lookup[-1140078], PeStringTable.Read(Image(culture), language)![8441]);
    }

    [RevitInstallTheory]
    [InlineData("ru-RU", 1049)]
    [InlineData("en-US", 1033)]
    public void AllOriginalBuiltinCaptionKeysDifferFromIfcNameOverrides(string culture, ushort language)
    {
        var table = PeStringTable.Read(Image(culture), language)!;
        string[] lines = File.ReadAllLines(Path.Combine(ResourceRoot(), "GeneratedResourceIds.inl"));
        string[] parameters = lines.Where(line => line.StartsWith("#define String_Generated_Param_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3351, parameters.Length);
        foreach (string line in parameters)
        {
            int id = int.Parse(line[(line.LastIndexOf(' ') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(table.TryGetValue(id, out string? caption));
            Assert.False(string.IsNullOrWhiteSpace(caption));
            Assert.DoesNotContain('\0', caption!);
            // Exact comparer and space removal used by the original parameter cache.
            string key = caption!.Replace(" ", string.Empty, StringComparison.Ordinal);
            Assert.False(StringComparer.InvariantCultureIgnoreCase.Equals(key, "IfcName"), line);
            Assert.False(StringComparer.InvariantCultureIgnoreCase.Equals(key, "NameOverride"), line);
        }
    }

    [RevitInstallFact]
    public void OriginalSymbolIdsAgreeWithNativeInitializerForSixOwnFamilyParameters()
    {
        string[] lines = File.ReadAllLines(Path.Combine(ResourceRoot(), "GeneratedResourceIds.inl"));
        Assert.Equal(4095, lines.Length);
        foreach (var (symbol, id) in new[]
        {
            ("RBS_ELEC_PANEL_NAME", 8441), ("RBS_ELEC_PANEL_MCB_RATING_PARAM", 8440),
            ("RBS_ELEC_PANEL_SUBFEED_LUGS_PARAM", 8448), ("RBS_ELEC_PANEL_FEED_THRU_LUGS_PARAM", 8436),
            ("RBS_ELEC_MOUNTING", 8415), ("RBS_ELEC_ENCLOSURE", 8403),
        })
            Assert.Single(lines, line => line == $"#define String_Generated_Param_{symbol} {id}");
    }


    private static readonly string[] IfcNameKeyHashes = [FrozenFixtures.Sha256("IFCNAME"), FrozenFixtures.Sha256("NAMEOVERRIDE"), FrozenFixtures.Sha256("")];

    /// <summary>The exact captions of the six parameters of the electrical panel family, in both languages, as SHA-256 hashes; the
    /// English ones are also what the embedded table of labels holds for them.</summary>
    [Theory]
    [InlineData("ru-RU", 8441, "480673cf433903e00c59955d7c1feab4de01ee80bb4f47db66511a62d143af6b")]
    [InlineData("en-US", 8441, "01677f2d930257c7de0963d070cc8e009a6c28d550e0df80b1ba4097f3ec0e75")]
    [InlineData("ru-RU", 8440, "ecd7d92480804df12110be41a22cc7d60c4957da6c85c0c968908b8a74068bbd")]
    [InlineData("en-US", 8440, "2c6b3789adccfe5f50d12bd087dbddb7c7c414ed077a609b4d8134396f776ab4")]
    [InlineData("ru-RU", 8448, "11e66c9f2c0c2b04b52b72e5eb615c65b326d5508aa7cb440a13987dd45289d6")]
    [InlineData("en-US", 8448, "7bab012351a1a31e93fd722c0b499d10e0ef130dd2220c3e7b7071bccb88695b")]
    [InlineData("ru-RU", 8436, "45af72cba0d73762f38b017a7774b5ba15b29887685af8595c5be6be62f15f8b")]
    [InlineData("en-US", 8436, "9ee14aae2f33b8fad97b08ac95ae1a81794d9ac55bdd80248bb955271a72a69b")]
    [InlineData("ru-RU", 8415, "39f3038937116dbc67ba2b245f282adc0a0b2950a4fbf9e123c3d29932f51f63")]
    [InlineData("en-US", 8415, "8b36c59d1d612acc4e44a9d10a7c3529430a5dade8cabc82aea2fe6046e45872")]
    [InlineData("ru-RU", 8403, "d87efdf031b4fe53b056eb22ee9c06ddc067fa66785ccea159aa59bdd267f0f2")]
    [InlineData("en-US", 8403, "037ef7f04300fea77217d1276f4fb411cc7f863ec4a9ff4e0392cc8b8ac60cc8")]
    public void FrozenCaptionOfAnOwnFamilyParameterIsThatOfTheOriginal(string culture, int id, string expectedHash)
    {
        var row = FrozenFixtures.Rows("own_family_captions.tsv", 4).Single(r => r[0] == culture && FrozenFixtures.Int(r[2]) == id);
        Assert.Equal(expectedHash, row[3]);
        if (culture == "en-US")
        {
            Assert.True(CompactLabels.TryGetParameterLabel((int)Enum.Parse<BuiltInParameter>(row[1]), out string label), row[1]);
            Assert.Equal(expectedHash, FrozenFixtures.Sha256(label));
        }
    }

    [Theory]
    [InlineData("en-US", 2)]
    [InlineData("ru-RU", 3)]
    public void FrozenCaptionKeysOfAll3351ParametersDifferFromIfcNameOverrides(string culture, int column)
    {
        var rows = FrozenFixtures.Rows("caption_keys.tsv", 4);
        Assert.Equal(3351, rows.Count);
        Assert.Equal(3351, rows.Select(r => r[0]).Distinct().Count());
        Assert.Equal(3351, rows.Select(r => r[1]).Distinct().Count());
        foreach (var row in rows)
        {
            Assert.Matches("^[0-9a-f]{64}$", row[column]);
            Assert.True(!IfcNameKeyHashes.Contains(row[column]), $"{culture} {row[0]}");
        }
    }

    /// <summary>The embedded English labels, the same names the original table has, are no name override either, and each has the key of
    /// the original caption of its string id.</summary>
    [Fact]
    public void EmbeddedLabelsDifferFromIfcNameOverridesAndHaveTheOriginalKeys()
    {
        var embedded = FrozenFixtures.EmbeddedParameters();
        Assert.Equal(3342, embedded.Count);
        foreach (var (id, _, label) in embedded)
        {
            string key = label.Replace(" ", string.Empty, StringComparison.Ordinal);
            Assert.False(StringComparer.InvariantCultureIgnoreCase.Equals(key, "IfcName"), id.ToString());
            Assert.False(StringComparer.InvariantCultureIgnoreCase.Equals(key, "NameOverride"), id.ToString());
        }
        var keyByResource = FrozenFixtures.Rows("caption_keys.tsv", 4).ToDictionary(r => FrozenFixtures.Int(r[1]), r => r[2]);
        foreach (var row in FrozenFixtures.Rows("parameter_table.tsv", 5).Where(r => r[3] == "1"))
        {
            Assert.True(CompactLabels.TryGetParameterLabel(FrozenFixtures.Int(row[0]), out string label), row[0]);
            Assert.Equal(keyByResource[FrozenFixtures.Int(row[2])], FrozenFixtures.Sha256(FrozenFixtures.Key(label)));
        }
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    public void EnumIdentifierIsNotTheFrozenNativeDefinitionCaption(string culture)
    {
        Assert.Equal("RBS_ELEC_PANEL_NAME", BipNames.Lookup[-1140078]);
        var row = FrozenFixtures.Rows("own_family_captions.tsv", 4).Single(r => r[0] == culture && r[1] == "RBS_ELEC_PANEL_NAME");
        Assert.NotEqual(FrozenFixtures.Sha256(BipNames.Lookup[-1140078]), row[3]);
    }

    [Fact]
    public void FrozenSymbolIdsAgreeWithNativeInitializerForSixOwnFamilyParameters()
    {
        var ids = FrozenFixtures.Rows("caption_keys.tsv", 4).ToDictionary(r => r[0], r => FrozenFixtures.Int(r[1]));
        foreach (var (symbol, id) in new[]
        {
            ("RBS_ELEC_PANEL_NAME", 8441), ("RBS_ELEC_PANEL_MCB_RATING_PARAM", 8440),
            ("RBS_ELEC_PANEL_SUBFEED_LUGS_PARAM", 8448), ("RBS_ELEC_PANEL_FEED_THRU_LUGS_PARAM", 8436),
            ("RBS_ELEC_MOUNTING", 8415), ("RBS_ELEC_ENCLOSURE", 8403),
        })
            Assert.Equal(id, ids[symbol]);
        Assert.Equal("4095", FrozenFixtures.Rows("original_pins.tsv", 4).Single(r => r[0] == "2022/GeneratedResourceIds.inl")[3]);
    }

    /// <summary>The length and hash of the original files the fixtures were derived from, written down once more here.</summary>
    [Theory]
    [InlineData("2022/ru-RU/GeneratedRes.dll", 195184, "36dcd286a1f4434d939eb79e598a8b6922e32d6f34f796971c471125646fd6ee")]
    [InlineData("2022/en-US/GeneratedRes.dll", 157640, "39dd0b16c9b1e72c204e1efa207e230782de192b984371e89d77dd3fd2989ca7")]
    [InlineData("2022/GeneratedResourceIds.inl", 269232, "1c14569f4e9a942a5919ff832cb31994c7f4ce45c2457f4cfee53644706c5234")]
    public void FrozenPinsRecordTheOriginalFiles(string relative, int length, string expectedHash)
    {
        var row = FrozenFixtures.Rows("original_pins.tsv", 4).Single(r => r[0] == relative);
        Assert.Equal(length, FrozenFixtures.Int(row[1]));
        Assert.Equal(expectedHash, row[2]);
    }

    /// <summary>With the original files at hand: every pinned file is unchanged.</summary>
    [RevitInstallFact]
    public void EveryPinnedOriginalFileIsUnchanged()
    {
        foreach (var row in FrozenFixtures.Rows("original_pins.tsv", 4))
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(RevitInstallResources.Root!, row[0]));
            Assert.Equal(FrozenFixtures.Int(row[1]), bytes.Length);
            Assert.Equal(row[2], Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }
}
