using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RevitReader.Tests;

/// <summary>The declared IFC length unit follows the stored Length format option of every unit layout, and an option that cannot be
/// used is reported with its reason.</summary>
public class ProjectLengthUnitTests
{
    private const string LengthSpec = "autodesk.spec.aec:length-1.0.0";

    private static TfBlockDeserializer.UnitOptions Options(params (string Spec, string Unit, byte UseDefault)[] options) =>
        new(options.Select(o => new TfBlockDeserializer.UnitFormatOption(o.Spec, "autodesk.unit.symbol:", o.Unit, 0.001, 0, 0, 0, 0, o.UseDefault, 0, 0)).ToArray(), 0, 0, 0, 0);

    [Theory]
    [InlineData("autodesk.unit.unit:meters-1.0.0", IfcUnitFamily.Metre)]
    [InlineData("autodesk.unit.unit:feetFractionalInches-1.0.0", IfcUnitFamily.Foot)]
    [InlineData("autodesk.unit.unit:millimeters-1.0.1", IfcUnitFamily.Millimetre)]
    public void StoredOption_GivesTheFamilyOfTheProject(string unit, IfcUnitFamily expected)
    {
        var options = Options(("autodesk.spec.aec:area-1.0.0", "autodesk.unit.unit:squareMeters-1.0.1", 0), (LengthSpec, unit, 0));
        Assert.Equal(unit, IfcLengthUnitChoice.StoredLengthUnit(options, out string? issue));
        Assert.Null(issue);
        Assert.Equal(expected, IfcLengthUnitChoice.Family(unit));
    }

    [Fact]
    public void UnusableOptions_AreNullWithTheirReason()
    {
        Assert.Null(IfcLengthUnitChoice.StoredLengthUnit(null, out string? issue));
        Assert.Contains("not read", issue);
        Assert.Null(IfcLengthUnitChoice.StoredLengthUnit(Options(("autodesk.spec.aec:area-1.0.0", "x", 0)), out issue));
        Assert.Contains("no Length", issue);
        Assert.Null(IfcLengthUnitChoice.StoredLengthUnit(Options((LengthSpec, "autodesk.unit.unit:meters-1.0.0", 1)), out issue));
        Assert.Contains("default", issue);
        Assert.Null(IfcLengthUnitChoice.StoredLengthUnit(Options((LengthSpec, "", 0)), out issue));
        Assert.Contains("no unit", issue);
    }

    private static IReadOnlyList<(string Class, Dictionary<string, object?> Values)> LegacyRecords(long displayUnit, long useDefault = 0) =>
    [
        ("AUnits", new() { ["m_universalUnitSystem"] = 1L }),
        ("FormatOptions", new() { ["m_displayUnits"] = displayUnit, ["m_bUseDefault"] = useDefault }),
        ("FormatOptions", new() { ["m_displayUnits"] = 12L, ["m_bUseDefault"] = 0L }),
    ];

    [Theory]
    [InlineData(0L, IfcUnitFamily.Metre)]
    [InlineData(2L, IfcUnitFamily.Millimetre)]
    [InlineData(3L, IfcUnitFamily.Foot)]
    [InlineData(4L, IfcUnitFamily.Foot)]
    [InlineData(6L, IfcUnitFamily.Millimetre)]
    public void DisplayUnitLayout_ReadsTheFirstOptionAsLength(long displayUnit, IfcUnitFamily expected)
    {
        string? unit = WideDocument.LegacyLengthUnit(LegacyRecords(displayUnit), out string? issue);
        Assert.Null(issue);
        Assert.Equal(expected, IfcLengthUnitChoice.Family(unit));
    }

    [Theory]
    [InlineData(12L, 0L, "not a length unit")]
    [InlineData(0L, 1L, "default")]
    public void DisplayUnitLayout_RejectsAnOptionThatIsNotAUsableLength(long displayUnit, long useDefault, string reason)
    {
        Assert.Null(WideDocument.LegacyLengthUnit(LegacyRecords(displayUnit, useDefault), out string? issue));
        Assert.Contains(reason, issue);
    }

    [Fact]
    public void DisplayUnitLayout_RejectsRecordsOfAnotherShape()
    {
        Assert.Null(WideDocument.LegacyLengthUnit([("AUnits", [])], out string? issue));
        Assert.NotNull(issue);
        Assert.Null(WideDocument.LegacyLengthUnit([("AUnits", []), ("ForgeTypeId", []), ("FormatOptions", [])], out issue));
        Assert.NotNull(issue);
    }

    private static string Export(string? lengthUnit, string? issue, out string diagnostics)
    {
        var diag = new StringWriter();
        var text = new StringWriter();
        new IfcExporter(diag).Export(new RevitFileInfo { SerializedLengthUnit = lengthUnit, SerializedLengthUnitIssue = issue }, text);
        diagnostics = diag.ToString();
        return text.ToString();
    }

    [Fact]
    public void MetreProject_DeclaresMetre()
    {
        string ifc = Export("autodesk.unit.unit:meters-1.0.0", null, out _);
        Assert.Contains("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)", ifc);
        Assert.DoesNotContain("'FOOT'", ifc);
    }

    [Fact]
    public void FootProject_DeclaresFoot()
    {
        string ifc = Export("autodesk.unit.unit:feet-1.0.1", null, out _);
        Assert.Contains("'FOOT'", ifc);
        Assert.DoesNotContain(".MILLI.,.METRE.", ifc);
    }

    [Fact]
    public void MillimetreProject_DeclaresMillimetre()
    {
        string ifc = Export("autodesk.unit.unit:millimeters-1.0.1", null, out string diagnostics);
        Assert.Contains("IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)", ifc);
        Assert.DoesNotContain("unreadable", diagnostics);
    }

    [Fact]
    public void UnreadableOption_FallsBackToMillimetresAndSaysWhy()
    {
        string ifc = Export(null, "no Length format option", out string diagnostics);
        Assert.Contains("IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)", ifc);
        Assert.Contains("unreadable (no Length format option); using the default Millimetre", diagnostics);
    }
}
