using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>A file of the 64-bit-id layout (<see cref="RevitFileInfo.GeometryUndecoded"/>) has no decoded geometry: the exporter writes the products that pass the data filters without a representation, a project without a building-storey level has no storey (current profile) and the products belong to the building, and the phase with element id 0 is a phase.</summary>
public class IfcExporterGeometryUndecodedTests
{
    [Fact]
    public void ProductWithoutGeometry_IsWrittenWithoutRepresentation_InTheBuilding()
    {
        var info = new RevitFileInfo { GeometryUndecoded = true };
        info.Elements.Add(Column(id: 100, phase: 3));
        string ifc = Export(info);

        var column = Assert.Single(Regex.Matches(ifc, @"#(\d+)=IFCCOLUMN\('[^']*',#\d+,.*,#\d+,\$,'100'\);"));
        Assert.DoesNotContain("IFCBUILDINGSTOREY", ifc);
        int buildingId = int.Parse(Assert.Single(Regex.Matches(ifc, @"#(\d+)=IFCBUILDING\(")).Groups[1].Value);
        var containment = Assert.Single(Regex.Matches(ifc, @"IFCRELCONTAINEDINSPATIALSTRUCTURE\('[^']*',#\d+,(?:\$|''),(?:\$|''),\(([^)]*)\),#(\d+)\);"));
        Assert.Equal("#" + column.Groups[1].Value, containment.Groups[1].Value);
        Assert.Equal(buildingId, int.Parse(containment.Groups[2].Value));
        Assert.Contains("IFCINTEGER(0)", ifc);
    }

    [Fact]
    public void ProductWithoutGeometry_IsNotWritten_WhenTheGeometryWasDecodedAsAbsent()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Column(id: 100, phase: 3));
        Assert.DoesNotContain("IFCCOLUMN(", Export(info));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void PhaseWithElementIdZero_IsAPhaseOnlyInTheWideLayout(bool wide, int expected)
    {
        var info = new RevitFileInfo { GeometryUndecoded = wide };
        info.Elements.Add(Column(id: 100, phase: 0));
        Assert.Equal(expected, Regex.Matches(Export(info), @"=IFCCOLUMN\(").Count);
    }

    private static string Export(RevitFileInfo info)
    {
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-undecoded-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Element Column(int id, int phase) => new()
    {
        Id = id,
        TypeId = id + 1000,
        CategoryId = -2001330,
        Category = "OST_StructuralColumns",
        Name = "Column",
        SerializedCreatedPhaseId = phase,
        Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = phase }],
    };
}
