using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The family-instance export hands the body exporter of a beam (column, member, pile) the direction of its structural axis, the line of the curve
/// driver, as the first axis to extrude along, then x and y (<c>TryXY</c>); z when it has no structural axis. The direction is the one of the
/// world and meets the faces of the solid as they are stored, so a family symbol turned about z is extruded across its width.
/// </summary>
public class IfcExporterBeamAxesCurrentTests
{
    private static Element Beam(bool line, bool driver)
    {
        var beam = new Element { Id = 1, CategoryId = -2001320 };
        if (line) beam.CurveDriverLineDirection = (0, 1, 0);
        else if (driver) beam.CurveDriverRefLevelId = 5;
        return beam;
    }

    [Fact]
    public void ABeamWithAStraightAxis_TriesTheAxisThenXAndY()
    {
        var axes = new IfcExporter().OthersCurrentSymbolAxes(Beam(line: true, driver: true), "IFCBEAM")!;
        Assert.Equal([[0, 1, 0], [1, 0, 0], [0, 1, 0]], axes);
    }

    [Fact]
    public void AColumnWithoutAStructuralAxis_TriesZ()
    {
        var column = new Element { Id = 2, CategoryId = -2001330 };
        Assert.Equal(IfcExporter.OthersCurrentAxesZ, new IfcExporter().OthersCurrentSymbolAxes(column, "IFCCOLUMN"));
    }

    [Theory]
    [InlineData("IFCCOLUMN")]
    [InlineData("IFCMEMBER")]
    [InlineData("IFCPILE")]
    public void AColumnMemberOrPileWithADriverButNoLineDirection_KeepsZ(string ifcType)
    {
        // the custom axis of an arc driver (the x direction of the arc) is not decoded: the axes are those the source had before the beam rule
        var driven = new Element { Id = 3, CategoryId = -2001330, CurveDriverRotation = 0.5, CurveDriverRefLevelId = 5 };
        Assert.Equal(IfcExporter.OthersCurrentAxesZ, new IfcExporter().OthersCurrentSymbolAxes(driven, ifcType));
    }

    [Fact]
    public void ABeamWithoutAnyDecodedDriver_IsNotModelled()
    {
        Assert.Null(new IfcExporter().OthersCurrentSymbolAxes(Beam(line: false, driver: false), "IFCBEAM"));
    }

    [Fact]
    public void ABeamDrivenByAnotherCurve_IsNotModelled()
    {
        Assert.Null(new IfcExporter().OthersCurrentSymbolAxes(Beam(line: false, driver: true), "IFCBEAM"));
    }

    [Fact]
    public void OtherFamilies_AndTheOlderDialect_KeepTheirAxes()
    {
        var exporter = new IfcExporter();
        Assert.Equal(IfcExporter.OthersCurrentAxesXyz, exporter.OthersCurrentSymbolAxes(new Element(), "IFCFURNISHINGELEMENT"));
        Assert.Null(new IfcExporter { Profile = IfcProfile.Legacy }.OthersCurrentSymbolAxes(Beam(line: true, driver: true), "IFCBEAM"));
    }

    // a box 10 long on x, 0.4 on y, 0.6 on z
    private static GeometryData Box()
    {
        double[][] p = Enumerable.Range(0, 8).Select(i => new double[] { (i & 1) * 10, ((i >> 1) & 1) * 0.4, ((i >> 2) & 1) * 0.6 }).ToArray();
        int[][] faces = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];
        return new GeometryData
        {
            BoundingBox = [0, 0, 0, 10, 0.4, 0.6],
            Faces = faces.Select(f => new GeoFace { OuterVertices = f.Select(i => p[i]).ToList() }).ToList(),
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }],
        };
    }

    [Fact]
    public void TheAxisOfTheWorldIsMetWithTheFacesAsStored()
    {
        var exporter = new IfcExporter();
        var geometry = Box();
        // the beam runs along x: the axis finds the pair of end faces
        var alongX = Assert.IsType<IfcExporter.OthersCurrentPlan>(exporter.OthersCurrentGetPlan(geometry, exporter.OthersCurrentSymbolAxes(
            new Element { CurveDriverLineDirection = (1, 0, 0) }, "IFCBEAM")!));
        Assert.Equal(10, alongX.Extrusions[0].LengthFt, 9);
        // the symbol is turned about z (the line runs along -y): the same box is extruded across its width, as the expected export does
        var alongY = Assert.IsType<IfcExporter.OthersCurrentPlan>(exporter.OthersCurrentGetPlan(geometry, exporter.OthersCurrentSymbolAxes(
            new Element { CurveDriverLineDirection = (0, -1, 0) }, "IFCBEAM")!));
        Assert.Equal(0.4, alongY.Extrusions[0].LengthFt, 9);
        Assert.Equal([0, -1, 0], alongY.Extrusions[0].Axis);
    }

    [Fact]
    public void ADirectBeamBody_IsExtrudedAlongItsStructuralAxis()
    {
        var geometry = Box();
        var beam = new Element
        {
            Id = 7, TypeId = 8, IsDiscovered = true, CategoryId = -2001320, Category = "OST_StructuralFraming", Name = "Beam",
            CurveDriverLineDirection = (1, 0, 0),
            References = [3],
            Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
            // an envelope that is off centre keeps the nominal beam of the older dialect out
            BoundingBox = [0, 0.1, 0, 10, 0.5, 0.6],
            Geometry = geometry,
        };
        var info = new RevitFileInfo();
        info.Elements.Add(beam);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-beam-axes-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);
            Assert.Contains("=IFCBEAM(", ifc);
            Assert.Single(Regex.Matches(ifc, "=IFCEXTRUDEDAREASOLID[(]"));
            Assert.DoesNotContain("=IFCFACETEDBREP(", ifc);
            var solid = Regex.Match(ifc, @"=IFCEXTRUDEDAREASOLID[(]#\d+,#\d+,#\d+,([^)]*)[)];");
            Assert.True(solid.Success, string.Join(" | ", ifc.Split((char)10).Where(l => l.Contains("EXTRUDED") || l.Contains("IFCBEAM("))));
            Assert.Equal(3048, double.Parse(solid.Groups[1].Value.TrimEnd('.'), System.Globalization.CultureInfo.InvariantCulture), 6);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
