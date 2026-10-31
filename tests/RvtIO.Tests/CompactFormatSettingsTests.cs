using System.Reflection;
using System.Text.Json.Serialization;
using RvtIO.Compact;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The provider of the document's unit format options for the display-value formatter: what it
/// takes from the persisted unit schemas and the units element, and what a real document gives.</summary>
public class CompactFormatSettingsTests : IClassFixture<CompactFormatSettingsTests.TestFileInfo>
{
    private const string UnitPrefix = "autodesk.unit.unit:";
    private const string SymbolPrefix = "autodesk.unit.symbol:";
    private static readonly UnitSchemaEvaluator.UnitConversions Sdk = UnitSchemaEvaluator.EvaluateDisplay(
        new(0, 0, []), out _) ?? throw new InvalidDataException("SDK numeric display unavailable");

    public sealed class TestFileInfo
    {
        internal RevitFileInfo Info { get; }

        public TestFileInfo()
            => Info = new RvtFileReader().Read(TestFileFixture.FindTestFile("TESTFILE.rvt"));
    }

    private readonly TestFileInfo _file;

    public CompactFormatSettingsTests(TestFileInfo file) => _file = file;

    private static TfBlockDeserializer.UnitFormatOption Option(string spec, string unit = "millimeters-1.0.1", string symbol = "")
        => new(spec, symbol, UnitPrefix + unit, 1, 0, 0, 0, 0, 0, 0, 0);

    private static TfBlockDeserializer.UnitsElemOptions Units(params TfBlockDeserializer.UnitFormatOption[] options)
        => new(77, -1, 1, 10, 50, new TfBlockDeserializer.UnitOptions(options, 1, 1, 2, 0));

    private static string SymbolJson(string id, string? text, string placement, bool space)
        => "{ \"typeid\": \"" + id + "\", \"inherits\": [ \"autodesk.unit:symbol.prefixOrSuffix-1.0.0\" ], \"constants\": [ "
           + "{ \"id\": \"unit\", \"typedValue\": { \"typeid\": \"autodesk.unit.unit:millimeters-1.0.1\" } }"
           + (text == null ? "" : ", { \"id\": \"text\", \"value\": \"" + text + "\" }")
           + ", { \"id\": \"placement\", \"value\": \"" + placement + "\" }, { \"id\": \"space\", \"value\": "
           + (space ? "true" : "false") + " } ] }";

    private static (TfBlockDeserializer.DocumentSchemaMap Units, TfBlockDeserializer.DocumentSchemaMap Parameters) Schemas(
        params (string Key, string Value)[] units)
        => (new(0, 0, units.Select(u => new TfBlockDeserializer.DocumentSchemaEntry(u.Key, u.Value, 0, 0)).ToArray()),
            new(0, 0, []));

    [SkippableFact]
    public void AnyMissingInputGivesNoSettings()
    {
        var units = Units(Option("autodesk.spec.aec:length-1.0.0"));
        var schemas = Schemas();
        Assert.NotNull(CompactFormatSettings.Create(units, Sdk, schemas));
        Assert.Null(CompactFormatSettings.Create(null, Sdk, schemas));
        Assert.Null(CompactFormatSettings.Create(units, null, schemas));
        Assert.Null(CompactFormatSettings.Create(units, Sdk, null));
    }

    [SkippableFact]
    public void SymbolLabelsComeFromThePersistedSchemas()
    {
        var settings = CompactFormatSettings.Create(Units(Option("autodesk.spec.aec:length-1.0.0")), Sdk, Schemas(
            (SymbolPrefix + "mm-1.0.1", SymbolJson(SymbolPrefix + "mm-1.0.1", "mm", "Suffix", true)),
            (SymbolPrefix + "usDollar-1.0.0", SymbolJson(SymbolPrefix + "usDollar-1.0.0", "$", "Prefix", false)),
            (SymbolPrefix + "noText-1.0.0", SymbolJson(SymbolPrefix + "noText-1.0.0", null, "Suffix", true)),
            (SymbolPrefix + "broken-1.0.0", "{ not json"),
            (SymbolPrefix + "badPlacement-1.0.0", SymbolJson(SymbolPrefix + "badPlacement-1.0.0", "x", "Middle", true)),
            (UnitPrefix + "millimeters-1.0.1", "{ \"typeid\": \"" + UnitPrefix + "millimeters-1.0.1\", \"constants\": [] }")))!;
        Assert.Equal(new CompactSymbolLabel("mm", false, true), settings.Symbols[SymbolPrefix + "mm-1.0.1"]);
        Assert.Equal(new CompactSymbolLabel("$", true, false), settings.Symbols[SymbolPrefix + "usDollar-1.0.0"]);
        Assert.Equal(2, settings.Symbols.Count);
    }

    [SkippableFact]
    public void TheSlopeSymbolIsTheDegreeSignAndOnlyWhenTheDegreeSymbolIsStored()
    {
        var degree = (SymbolPrefix + "degree-1.0.1", SymbolJson(SymbolPrefix + "degree-1.0.1", "°", "Suffix", false));
        var units = Units(Option("autodesk.spec.aec:slope-1.0.0", "slopeDegrees-1.0.0", SymbolPrefix + "slopeDegree-1.0.0") with { Accuracy = 0.01 });
        var withDegree = CompactFormatSettings.Create(units, Sdk, Schemas(degree))!;
        Assert.Equal(new CompactSymbolLabel("°", false, false), withDegree.Symbols[SymbolPrefix + "slopeDegree-1.0.0"]);
        Assert.Equal("15,00°", CompactValueFormatter.Format(0.2679491924311224, "autodesk.spec.aec:slope-1.0.0", withDegree));
        Assert.False(CompactFormatSettings.Create(units, Sdk, Schemas())!.Symbols.ContainsKey(SymbolPrefix + "slopeDegree-1.0.0"));
        var own = (SymbolPrefix + "slopeDegree-1.0.0", SymbolJson(SymbolPrefix + "slopeDegree-1.0.0", "s", "Suffix", true));
        Assert.Equal("s", CompactFormatSettings.Create(units, Sdk, Schemas(degree, own))!.Symbols[SymbolPrefix + "slopeDegree-1.0.0"].Text);
    }

    [SkippableFact]
    public void ASpecIsFoundByItsNameAndAnAmbiguousNameIsNotGuessed()
    {
        var settings = CompactFormatSettings.Create(Units(
            Option("autodesk.spec.aec:length-1.0.0"), Option("autodesk.spec.aec:area-1.0.0", "squareMeters-1.0.1"),
            Option("autodesk.spec.aec:slope-1.0.0"), Option("autodesk.spec.aec:slope-2.0.0", "degrees-1.0.1")), Sdk, Schemas())!;
        Assert.Equal(UnitPrefix + "millimeters-1.0.1", settings.Find("autodesk.spec.aec:length-2.0.1")!.Unit);
        Assert.Equal(UnitPrefix + "millimeters-1.0.1", settings.Find("autodesk.spec.aec:length")!.Unit);
        Assert.Equal(UnitPrefix + "squareMeters-1.0.1", settings.Find("autodesk.spec.aec:area-2.0.0")!.Unit);
        Assert.Null(settings.Find("autodesk.spec.aec:volume-2.0.0"));
        Assert.Null(settings.Find("autodesk.spec.aec:slope-1.0.0"));
        Assert.Null(settings.Find("autodesk.spec.aec.hvac:length-1.0.0"));
    }

    [SkippableFact]
    public void TheSettingsAreInternalAndNeverSerialised()
    {
        var property = typeof(RevitFileInfo).GetProperty(nameof(RevitFileInfo.ElemTableIsExact), BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Null(typeof(RevitFileInfo).GetProperty("SerializedFormatSettings", BindingFlags.Public | BindingFlags.Instance));
        var own = typeof(RevitFileInfo).GetProperty("SerializedFormatSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(own);
        Assert.NotNull(own.GetCustomAttribute<JsonIgnoreAttribute>());
    }

    [SkippableFact]
    public void ARealDocumentGivesItsOwnOptionsAndSeparators()
    {
        var settings = _file.Info.SerializedFormatSettings;
        Assert.NotNull(settings);
        Assert.Equal((1, 0, 1, 1), (settings.Options.UniversalUnitSystem, settings.Options.DecimalSymbol,
            settings.Options.DigitGroupingSymbol, settings.Options.DigitGroupingAmount));
        Assert.Equal(135, settings.Options.Options.Length);
        var length = settings.Find("autodesk.spec.aec:length-2.0.1");
        Assert.NotNull(length);
        Assert.Equal((UnitPrefix + "millimeters-1.0.1", "", 1.0), (length.Unit, length.Symbol, length.Accuracy));
        Assert.Equal(SymbolPrefix + "mSup2-1.0.1", settings.Find("autodesk.spec.aec:area-2.0.0")!.Symbol);
        Assert.Equal("m²", settings.Symbols[SymbolPrefix + "mSup2-1.0.1"].Text);
        Assert.Contains(SymbolPrefix + "slopeDegree-1.0.0", settings.Symbols.Keys);
    }

    // Expected strings: the expected export label utilities run on the same document.
    [SkippableTheory]
    [InlineData("autodesk.spec.aec:length-2.0.1", 0.4921259842519685, "150")]
    [InlineData("autodesk.spec.aec:length-2.0.1", 1.0, "305")]
    [InlineData("autodesk.spec.aec:length-2.0.1", 3.280839895013123, "1000")]
    [InlineData("autodesk.spec.aec:length-2.0.1", -0.5, "-152")]
    [InlineData("autodesk.spec.aec:length-2.0.1", 1e-12, "0")]
    [InlineData("autodesk.spec.aec:area-2.0.0", 10.763910416709722, "1 m²")]
    [InlineData("autodesk.spec.aec:area-2.0.0", 1139.962696592106, "106 m²")]
    [InlineData("autodesk.spec.aec:area-2.0.0", -0.0929, "-0 m²")]
    [InlineData("autodesk.spec.aec:volume-2.0.0", 561.0052640709184, "15.89 m³")]
    [InlineData("autodesk.spec.aec:volume-2.0.0", 0.0, "0.00 m³")]
    [InlineData("autodesk.spec.aec:angle-2.0.0", 0.7853981633974483, "45.00°")]
    [InlineData("autodesk.spec.aec:angle-2.0.0", Math.PI, "180.00°")]
    [InlineData("autodesk.spec.aec:angle-2.0.0", -1.5707963267948966, "-90.00°")]
    [InlineData("autodesk.spec.aec:number-1.0.1", 0.1, "0.1")]
    [InlineData("autodesk.spec.aec:number-1.0.1", Math.PI, "3.141593")]
    [InlineData("autodesk.spec.aec:number-1.0.1", 1e6, "1000000")]
    public void ARealDocumentFormatsLikeReference(string spec, double value, string expected)
        => Assert.Equal(expected, CompactValueFormatter.Format(value, spec, _file.Info.SerializedFormatSettings!));

    [SkippableFact]
    public void ASpecTheDocumentDoesNotStoreIsNotFormatted()
        => Assert.Null(CompactValueFormatter.Format(1.0, "autodesk.spec.aec.electrical:apparentPowerDensity-1.0.0",
            _file.Info.SerializedFormatSettings!));
}
