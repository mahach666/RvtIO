using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The current profile header data: the unit assignment (millimetre, DEGREE as a conversion based unit of the radian, square metre,
/// cubic metre, and the thermal transmittance and power units only when a value of that measure exists), degree-valued plane angles, and the
/// project and building names. The legacy profile keeps radians and seconds.</summary>
public class IfcUnitAssignmentCurrentTests
{
    private static string ExportText(IfcProfile version, RevitFileInfo info)
    {
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-units-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static RevitFileInfo LevelOnly()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = 10, CategoryId = -2000240, Category = "OST_Levels", Name = "Level 1" });
        return info;
    }

    /// <summary>The text of each entity the IFCUNITASSIGNMENT lists, in list order.</summary>
    private static List<string> AssignedUnits(string ifc)
    {
        var entities = Regex.Matches(ifc, @"^#(\d+)=(\w+)\((.*)\);\r?$", RegexOptions.Multiline)
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => $"{m.Groups[2].Value}({m.Groups[3].Value})");
        string assignment = Regex.Match(ifc, @"=IFCUNITASSIGNMENT\(\(([^)]*)\)\);").Groups[1].Value;
        return assignment.Split(',').Select(reference => entities[int.Parse(reference.TrimStart('#'))]).ToList();
    }

    [Fact]
    public void ExportCurrent_AssignsMillimetreDegreeSquareMetreAndCubicMetreOnly()
    {
        string ifc = ExportText(IfcProfile.Current, LevelOnly());
        List<string> units = AssignedUnits(ifc);

        Assert.Equal(4, units.Count);
        Assert.Equal("IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)", units[0]);
        Assert.Matches(@"^IFCCONVERSIONBASEDUNIT\(#\d+,\.PLANEANGLEUNIT\.,'DEGREE',#\d+\)$", units[1]);
        Assert.Equal("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)", units[2]);
        Assert.Equal("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)", units[3]);
        // The degree is the radian times 0.017453292519943278 with no exponents.
        Assert.Contains("=IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.);", ifc);
        Assert.Contains("=IFCDIMENSIONALEXPONENTS(0,0,0,0,0,0,0);", ifc);
        Assert.Matches(@"=IFCMEASUREWITHUNIT\(IFCPLANEANGLEMEASURE\(0\.017453292519943278\),#\d+\);", ifc);
        // No time unit is assigned, and with no thermal or power value there is no derived unit or watt.
        Assert.DoesNotContain(".TIMEUNIT.", ifc);
        Assert.DoesNotContain("IFCDERIVEDUNIT", ifc);
        Assert.DoesNotContain(".WATT.", ifc);
    }

    [Fact]
    public void ExportLegacy_KeepsTheRadianAndTheSecond()
    {
        string ifc = ExportText(IfcProfile.Legacy, LevelOnly());
        List<string> units = AssignedUnits(ifc);

        Assert.Equal(
            ["IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)", "IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)",
             "IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)", "IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)",
             "IFCSIUNIT(*,.TIMEUNIT.,$,.SECOND.)"], units);
        Assert.DoesNotContain("IFCCONVERSIONBASEDUNIT", ifc);
    }

    private static (StepWriter Writer, IfcExporter.UnitAssignmentCurrent Units) NewWriter()
    {
        var w = new StepWriter();
        int length = w.Add("IFCSIUNIT", "*,.LENGTHUNIT.,.MILLI.,.METRE.");
        return (w, IfcExporter.CreateUnitAssignmentCurrent(w, length));
    }

    private static List<string> Assigned(StepWriter w, IfcExporter.UnitAssignmentCurrent units)
    {
        var byId = Enumerable.Range(0, w.EntityCount).Select(w.EntityAt).ToDictionary(e => e.Id, e => $"{e.Type}({e.Args})");
        string args = w.EntityAt(units.AssignmentIndex).Args;
        return args[1..^1].Split(',').Select(reference => byId[int.Parse(reference.TrimStart('#'))]).ToList();
    }

    [Fact]
    public void Finalize_AddsThermalTransmittanceAsKilogramKelvinSecondDerivedUnit()
    {
        var (w, units) = NewWriter();
        w.Add("IFCPROPERTYSINGLEVALUE", "'ThermalTransmittance','',IFCTHERMALTRANSMITTANCEMEASURE(3.6886),$");
        IfcExporter.FinalizeUnitAssignmentCurrent(w, units);

        List<string> assigned = Assigned(w, units);
        Assert.Equal(5, assigned.Count);
        Assert.Matches(@"^IFCDERIVEDUNIT\(\(#\d+,#\d+,#\d+\),\.THERMALTRANSMITTANCEUNIT\.,''\)$", assigned[4]);
        string text = string.Join('\n', Enumerable.Range(0, w.EntityCount).Select(i => $"{w.EntityAt(i).Type}({w.EntityAt(i).Args})"));
        Assert.Contains("IFCSIUNIT(*,.MASSUNIT.,.KILO.,.GRAM.)", text);
        Assert.Contains("IFCSIUNIT(*,.THERMODYNAMICTEMPERATUREUNIT.,$,.KELVIN.)", text);
        Assert.Contains("IFCSIUNIT(*,.TIMEUNIT.,$,.SECOND.)", text);
        Assert.Matches(@"IFCDERIVEDUNITELEMENT\(#\d+,1\)", text);
        Assert.Matches(@"IFCDERIVEDUNITELEMENT\(#\d+,-1\)", text);
        Assert.Matches(@"IFCDERIVEDUNITELEMENT\(#\d+,-3\)", text);
    }

    [Fact]
    public void Finalize_AssignsTheWattAndOrdersThermalAndPowerByFirstValue()
    {
        var (w, units) = NewWriter();
        w.Add("IFCPROPERTYSINGLEVALUE", "'TotalWattage','',IFCPOWERMEASURE(0.),$");
        w.Add("IFCPROPERTYSINGLEVALUE", "'ThermalTransmittance','',IFCTHERMALTRANSMITTANCEMEASURE(3.6886),$");
        IfcExporter.FinalizeUnitAssignmentCurrent(w, units);

        List<string> assigned = Assigned(w, units);
        Assert.Equal(6, assigned.Count);
        Assert.Equal("IFCSIUNIT(*,.POWERUNIT.,$,.WATT.)", assigned[4]);
        Assert.StartsWith("IFCDERIVEDUNIT(", assigned[5]);
    }

    [Fact]
    public void Finalize_IgnoresMeasuresOutsideAPropertyValue()
    {
        var (w, units) = NewWriter();
        w.Add("IFCLABEL", "'IFCPOWERMEASURE( IFCTHERMALTRANSMITTANCEMEASURE('");
        IfcExporter.FinalizeUnitAssignmentCurrent(w, units);

        Assert.Equal(4, Assigned(w, units).Count);
    }

    [Fact]
    public void Finalize_PutsAreaAndVolumeBeforeTheDegreeWhenSpacesAreExported()
    {
        // Spatial elements are exported first, their quantities create the area and volume units, the first extrusion creates the degree.
        // Without a space the extrusion comes first.
        var (plain, plainUnits) = NewWriter();
        IfcExporter.FinalizeUnitAssignmentCurrent(plain, plainUnits);
        Assert.Equal(["LENGTHUNIT", "PLANEANGLEUNIT", "AREAUNIT", "VOLUMEUNIT"],
            Assigned(plain, plainUnits).Select(UnitEnum).ToList());

        var (withSpace, spaceUnits) = NewWriter();
        withSpace.Add("IFCSPACE", "'guid',#5,'Room',$,$,$,$,$,.ELEMENT.,.INTERNAL.,0.");
        IfcExporter.FinalizeUnitAssignmentCurrent(withSpace, spaceUnits);
        Assert.Equal(["LENGTHUNIT", "AREAUNIT", "VOLUMEUNIT", "PLANEANGLEUNIT"],
            Assigned(withSpace, spaceUnits).Select(UnitEnum).ToList());
    }

    private static string UnitEnum(string unit) => Regex.Match(unit, @"\.(\w+UNIT)\.").Groups[1].Value;

    [Fact]
    public void Finalize_RejectsAnAssignmentThatIsNotWhereItWasWritten()
    {
        var (w, units) = NewWriter();
        Assert.Throws<InvalidOperationException>(() =>
            IfcExporter.FinalizeUnitAssignmentCurrent(w, units with { AssignmentIndex = units.AssignmentIndex - 1 }));
    }

    [Fact]
    public void TypedAngleParameter_IsDegreesInCurrentAndRadiansInLegacy()
    {
        var parameter = new Parameter { Id = -1, StorageType = StorageType.Double, Value = Math.PI / 2 };
        var element = new Element { Id = 1 };
        var elements = new Dictionary<int, Element>();

        Assert.Equal("IFCPLANEANGLEMEASURE(90.)",
            IfcExporter.TypedApiParameterValue(element, parameter, "autodesk.spec.aec:angle", elements, angleInDegrees: true));
        Assert.Equal("IFCPLANEANGLEMEASURE(1.5707963267949)",
            IfcExporter.TypedApiParameterValue(element, parameter, "autodesk.spec.aec:angle", elements));
    }

    private static Element ProjectInformation(params (int Id, string Value)[] texts)
    {
        var element = new Element { Id = 1281, CategoryId = -2003101, Category = "OST_ProjectInformation" };
        foreach (var (id, value) in texts)
            element.Parameters.Add(new Parameter { Id = id, StorageType = StorageType.String, Value = value });
        return element;
    }

    [Fact]
    public void ProjectAttributesCurrent_AreTheNumberNameAndStatusParametersAndEmptyWhenAbsent()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(ProjectInformation((-1006316, "1365"), (-1006317, "Project"), (-1006320, "P"), (-1019006, "Building")));
        Assert.Equal(("1365", "Project", "P"), IfcExporter.ResolveProjectAttributesCurrent(info));
        Assert.Equal("Building", IfcExporter.ResolveBuildingNameCurrent(info));

        // There is no fallback to another name: an empty number is the empty name (the legacy profile rule would use the project name).
        info = new RevitFileInfo { ProjectInfo = new ProjectInfo { Name = "From the stream" } };
        info.Elements.Add(ProjectInformation((-1006316, "No. 7")));
        Assert.Equal(("No. 7", "From the stream", ""), IfcExporter.ResolveProjectAttributesCurrent(info));
        Assert.Equal(("", "", ""), IfcExporter.ResolveProjectAttributesCurrent(new RevitFileInfo()));
        Assert.Null(IfcExporter.ResolveBuildingNameCurrent(new RevitFileInfo()));
    }

    [Fact]
    public void ProjectNameCurrent_WithoutANumberIsTheIdOfTheProjectInformationElement()
    {
        // PROJECT_NUMBER is empty and the project is named after the element (id 1365, no element name).
        var info = new RevitFileInfo { ProjectInfo = new ProjectInfo { Name = "Name" } };
        var projectInformation = ProjectInformation((-1006317, "Name"));
        projectInformation.Id = 1365;
        info.Elements.Add(projectInformation);
        Assert.Equal(("1365", "Name", ""), IfcExporter.ResolveProjectAttributesCurrent(info));

        // A number wins over the id; an empty number string is empty, not a number.
        projectInformation.Parameters.Add(new Parameter { Id = -1006316, StorageType = StorageType.String, Value = "7" });
        Assert.Equal("7", IfcExporter.ResolveProjectAttributesCurrent(info).Name);

        // Two project information elements leave no sole element to name the project after.
        info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = 1, CategoryId = -2003101 });
        info.Elements.Add(new Element { Id = 2, CategoryId = -2003101 });
        Assert.Equal("", IfcExporter.ResolveProjectAttributesCurrent(info).Name);

        // The legacy profile keeps its own fallback to the project name.
        var older = new RevitFileInfo { ProjectInfo = new ProjectInfo { Name = "Sample" } };
        older.Elements.Add(new Element { Id = 1365, CategoryId = -2003101 });
        Assert.Equal("Sample", IfcExporter.ResolveProjectAttributes(older).Name);
    }

    [Fact]
    public void ExportCurrent_WritesEmptyProjectAndBuildingStringsNotUnsetOnes()
    {
        string ifc = ExportText(IfcProfile.Current, LevelOnly());

        Assert.Matches(@"=IFCPROJECT\('[^']+',#\d+,'','','','','',\(#\d+\),#\d+\);", ifc);
        Assert.Matches(@"=IFCBUILDING\('[^']+',#\d+,'','','',#\d+,\$,'',\.ELEMENT\.,\$,\$,\$\);", ifc);
    }

    [Fact]
    public void ExportLegacy_KeepsTheProjectNameFallbackAndUnsetLongName()
    {
        var info = LevelOnly();
        info.ProjectInfo = new ProjectInfo { Name = "Sample House" };
        string ifc = ExportText(IfcProfile.Legacy, info);

        Assert.Matches(@"=IFCPROJECT\('[^']+',#\d+,'Sample House',\$,\$,\$,\$,\(#\d+\),#\d+\);", ifc);
        Assert.Matches(@"=IFCBUILDING\('[^']+',#\d+,'Building',\$,\$,#\d+,\$,\$,\.ELEMENT\.,\$,\$,\$\);", ifc);
    }
}
