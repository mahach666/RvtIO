using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The owner history, geometric sub-contexts and postal addresses of the current profile; the legacy profile keeps its header.</summary>
public class IfcExporterHeaderCurrentTests
{
    private static string ExportText(IfcProfile version, RevitFileInfo info, bool baseQuantities = false)
    {
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-header-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version, BaseQuantities = baseQuantities }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static Element ProjectInformation(params (int Id, string Value)[] texts)
    {
        var element = new Element { Id = 1281, CategoryId = -2003101, Category = "OST_ProjectInformation" };
        foreach (var (id, value) in texts)
            element.Parameters.Add(new Parameter { Id = id, StorageType = StorageType.String, Value = value });
        return element;
    }

    private static RevitFileInfo Project(params (int Id, string Value)[] texts)
    {
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = 10, CategoryId = -2000240, Category = "OST_Levels", Name = "Level 1" });
        info.Elements.Add(ProjectInformation(texts));
        return info;
    }

    [Theory]
    [InlineData("Samuel Macalister", "Macalister", "Samuel", "", "", "")]
    [InlineData("Иванова", "", "Иванова", "", "", "")]
    [InlineData("", "", "", "", "", "")]
    [InlineData("Dr. John A. Smith Jr.", "Smith", "John", "A.", "Dr.", "Jr.")]
    // After a comma the following words are put in front of what was read: "Macalister, Samuel" reads backwards.
    [InlineData("Macalister, Samuel", "Samuel", "Macalister", "", "", "")]
    [InlineData("  Ann   Marie  Jones ", "Jones", "Ann", "Marie", "", "")]
    public void ParsePersonNameCurrent_SplitsTitlesGivenMiddleAndFamilyNames(
        string author, string family, string given, string middle, string prefixes, string suffixes)
    {
        var parsed = IfcExporter.ParsePersonNameCurrent(author);

        Assert.Equal((family, given, middle, prefixes, suffixes), (parsed.Family, parsed.Given,
            string.Join(' ', parsed.Middle), string.Join(' ', parsed.Prefixes), string.Join(' ', parsed.Suffixes)));
    }

    [Fact]
    public void ParsePersonNameCurrent_ALeadingCommaDoesNotLoopForever()
    {
        var parsed = IfcExporter.ParsePersonNameCurrent(", Ivan");

        Assert.Equal(("", "Ivan"), (parsed.Family, parsed.Given));
    }

    [Theory]
    [InlineData("Enter address here", "Boston, MA", "$,'','','',('Enter address here'),'','','Boston','','MA'")]
    [InlineData("Адрес проекта", "Москва, Россия", "$,'','','',('\\X2\\04100434044004350441\\X0\\ \\X2\\043F0440043E0435043A04420430\\X0\\'),'','','\\X2\\041C043E0441043A04320430\\X0\\','','\\X2\\0420043E044104410438044F\\X0\\'")]
    // A region with a postal code, a town in front of it and a leftover line: "Street 5, Town, Region 12345, Country".
    [InlineData("", "Street 5, Town, Region 12345, Country", "$,'','','',('Street 5'),'','Town','Region','12345','Country'")]
    // No place at all: no lines either without a project address, every text empty.
    [InlineData("", null, "$,'','','',$,'','','','',''")]
    // One part is the country.
    [InlineData("Lane 1", "Atlantis", "$,'','','',('Lane 1'),'','','','','Atlantis'")]
    public void PostalAddressCurrent_ReadsTheProjectAddressAndThePlaceNameFromTheEnd(string address, string? place, string expected)
    {
        var info = Project((-1006318, address));
        info.SitePlaceName = place;

        Assert.Equal(expected, IfcExporter.PostalAddressArgumentsCurrent(info));
    }

    [Fact]
    public void ExportCurrent_OwnerHistoryBelongsToTheProjectAuthorAndOrganization()
    {
        var info = Project((-1019005, "Samuel Macalister"), (-1019008, "Autodesk"), (-1019007, "About the firm"));

        string ifc = ExportText(IfcProfile.Current, info);

        // developer organization first, the project's own after the person
        var organizations = Regex.Matches(ifc, @"^#(\d+)=IFCORGANIZATION\((.*)\);\r?$", RegexOptions.Multiline);
        Assert.Equal(2, organizations.Count);
        Assert.Equal("'','RvtIO','',$,$", organizations[0].Groups[2].Value);
        Assert.Equal("'','Autodesk','About the firm',$,$", organizations[1].Groups[2].Value);
        Match person = Regex.Match(ifc, @"^#(\d+)=IFCPERSON\('','Macalister','Samuel',\$,\$,\$,\$,\$\);\r?$", RegexOptions.Multiline);
        Assert.True(person.Success);
        Assert.Contains($"=IFCPERSONANDORGANIZATION(#{person.Groups[1].Value},#{organizations[1].Groups[1].Value},$);", ifc);
    }

    [Fact]
    public void ExportCurrent_WithoutProjectInformationThePersonAndOrganizationAreEmpty()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = 10, CategoryId = -2000240, Category = "OST_Levels", Name = "Level 1" });

        string ifc = ExportText(IfcProfile.Current, info);

        Assert.Contains("=IFCPERSON('','','',$,$,$,$,$);", ifc);
        Assert.Contains("=IFCORGANIZATION('','','',$,$);", ifc);
    }

    [Fact]
    public void ExportCurrent_TheStreamValuesStandInWhenTheElementHasNoParameter()
    {
        var info = new RevitFileInfo
        {
            ProjectInfo = new ProjectInfo { Author = "Ivan Petrov", OrganizationName = "Firm", Address = "Main St 1" },
        };
        info.Elements.Add(new Element { Id = 10, CategoryId = -2000240, Category = "OST_Levels", Name = "Level 1" });

        string ifc = ExportText(IfcProfile.Current, info);

        Assert.Contains("=IFCPERSON('','Petrov','Ivan',$,$,$,$,$);", ifc);
        Assert.Contains("=IFCORGANIZATION('','Firm','',$,$);", ifc);
        Assert.Contains("=IFCPOSTALADDRESS($,'','','',('Main St 1'),'','','','','');", ifc);
    }

    [Fact]
    public void ExportCurrent_WritesTheTwoPostalAddressesOfTheSiteAndTheBuildingAndAssignsNeither()
    {
        var info = Project((-1006318, "Enter address here"));
        info.SitePlaceName = "Boston, MA";

        string ifc = ExportText(IfcProfile.Current, info);

        var addresses = Regex.Matches(ifc, @"^#(\d+)=IFCPOSTALADDRESS\((.*)\);\r?$", RegexOptions.Multiline);
        Assert.Equal(2, addresses.Count);
        Assert.All(addresses, address => Assert.Equal("$,'','','',('Enter address here'),'','','Boston','','MA'", address.Groups[2].Value));
        // only the address's own definition names it
        foreach (Match address in addresses)
            Assert.Single(Regex.Matches(ifc, $@"#{address.Groups[1].Value}(?!\d)"));
        Assert.Matches(@"=IFCSITE\('[^']+',#\d+,'Default','','',#\d+,\$,'',\.ELEMENT\.,\$,\$,\$,'',\$\);", ifc);
        Assert.Matches(@"=IFCBUILDING\('[^']+',#\d+,'','','',#\d+,\$,'',\.ELEMENT\.,\$,\$,\$\);", ifc);
    }

    [Fact]
    public void ExportCurrent_WritesTheFourSubContextsOfCoordinationView2()
    {
        string ifc = ExportText(IfcProfile.Current, Project());

        var contexts = Regex.Matches(ifc, @"=IFCGEOMETRICREPRESENTATIONSUBCONTEXT\((.*?),'Model',\*,\*,\*,\*,#(\d+),\$,\.(\w+)\.,''\);")
            .Select(m => (Identifier: m.Groups[1].Value, View: m.Groups[3].Value)).ToList();

        Assert.Equal(
            [("'Body'", "MODEL_VIEW"), ("'Axis'", "GRAPH_VIEW"), ("'Box'", "MODEL_VIEW"), ("'FootPrint'", "MODEL_VIEW")],
            contexts);
        Assert.Contains("=IFCGEOMETRICREPRESENTATIONCONTEXT('','Model',3,", ifc);
    }

    [Fact]
    public void ExportCurrent_GridsAreRepresentedInTheFootPrintSubContext()
    {
        var info = Project();
        info.Elements.Add(new Element
        {
            Id = 1, TypeId = 1001, CategoryId = -2000240, Category = "OST_Levels", Name = "Ground", IsNativeLevel = true,
            SerializedFamilyId = -1, SerializedUnplacedOwnerId = -1, SerializedOwnerDbViewId = -1,
            Parameters =
            [
                new Parameter { Id = -1007102, Definition = new Definition { Name = "LEVEL_ELEV" }, StorageType = StorageType.Double, Value = 0.0 },
                new Parameter { Id = -1007111, StorageType = StorageType.Integer, Value = 1 },
            ],
        });
        info.Elements.Add(Grid(20, "A", 0, 0, 10, 0));
        info.Elements.Add(Grid(21, "1", 0, 0, 0, 10));

        string ifc = ExportText(IfcProfile.Current, info);

        string footPrint = Regex.Match(ifc, @"^#(\d+)=IFCGEOMETRICREPRESENTATIONSUBCONTEXT\('FootPrint'", RegexOptions.Multiline).Groups[1].Value;
        Assert.Matches($@"=IFCSHAPEREPRESENTATION\(#{footPrint},'FootPrint','GeometricCurveSet',", ifc);
    }

    [Fact]
    public void ExportLegacy_KeepsTheTwoSubContextsAndNoPostalAddress()
    {
        string ifc = ExportText(IfcProfile.Legacy, Project((-1019005, "Samuel Macalister"), (-1019008, "Autodesk")));

        Assert.Equal(2, Regex.Matches(ifc, "IFCGEOMETRICREPRESENTATIONSUBCONTEXT").Count);
        Assert.DoesNotContain("IFCPOSTALADDRESS", ifc);
        Assert.Contains("=IFCPERSON($,$,$,$,$,$,$,$);", ifc);
        Assert.Single(Regex.Matches(ifc, "=IFCORGANIZATION\\("));
    }

    [Fact]
    public void ExportCurrent_FileHeaderNamesTheProjectNumberAndOrganizationAndTheCoordinationView()
    {
        string withQuantities = ExportText(IfcProfile.Current, Project((-1006316, "001-00"), (-1019008, "Autodesk")), baseQuantities: true);
        string without = ExportText(IfcProfile.Current, Project());
        string old = ExportText(IfcProfile.Legacy, Project((-1006316, "001-00")));

        Assert.Contains("FILE_DESCRIPTION(('ViewDefinition [CoordinationView_V2.0, QuantityTakeOffAddOnView]'),'2;1');", withQuantities);
        Assert.Matches(@"FILE_NAME\('001-00','[^']+',\(''\),\('Autodesk'\),", withQuantities);
        Assert.Contains("FILE_DESCRIPTION(('ViewDefinition [CoordinationView_V2.0]'),'2;1');", without);
        Assert.Matches(@"FILE_NAME\('output\.ifc',", without);
        Assert.Contains("FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');", old);
        Assert.Matches(@"FILE_NAME\('output\.ifc',", old);
    }

    private static Element Grid(int id, string label, double x0, double y0, double x1, double y1) => new()
    {
        Id = id,
        CategoryId = -2000220,
        Category = "OST_Grids",
        Name = "6.5mm Bubble",
        Parameters = [new Parameter { Id = -1008000, StorageType = StorageType.String, Value = label }],
        Location = new LocationCurve { CurveType = "Line", StartPoint = [x0, y0, 4.0], EndPoint = [x1, y1, 4.0] },
    };
}
