using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

public class IfcExporterCircularGeometryTests
{
    [Fact]
    public void AnalyticCylinderProvenance_RecoversExactCircularExtrusion()
    {
        GeometryData geometry = CircularPrism(includeAnalyticProvenance: true);

        Assert.True(IfcExporter.TryGetExactCircularExtrusion(
            geometry, out var extrusion));
        Assert.Equal(0.25, extrusion.RadiusFt, 10);
        Assert.Equal(4, extrusion.LengthFt, 10);
        Assert.Equal([0, -2, 0.5], extrusion.StartCentreFt);
        Assert.Equal([0, 1, 0], extrusion.Axis);
        Assert.Equal([1, 0, 0], extrusion.ProfileX);
    }

    [Fact]
    public void RegularPolygonWithoutAnalyticProvenance_RemainsFaceted()
    {
        GeometryData geometry = CircularPrism(includeAnalyticProvenance: false);

        Assert.False(IfcExporter.TryGetExactCircularExtrusion(
            geometry, out _));
    }

    [Fact]
    public void AnalyticCylinderWithIncompleteCapTopology_RemainsFaceted()
    {
        GeometryData geometry = CircularPrism(includeAnalyticProvenance: true);
        geometry.BrepSolids![0].FaceIndices.RemoveAt(
            geometry.BrepSolids[0].FaceIndices.Count - 1);

        Assert.False(IfcExporter.TryGetExactCircularExtrusion(
            geometry, out _));
    }

    [Fact]
    public void ValidatedMappedCylinder_ExportsCircleSweptSolidInsideRepresentationMap()
    {
        const int symbolId = 201;
        GeometryData symbolGeometry = CircularPrism(includeAnalyticProvenance: true);
        GeometryData instanceGeometry = CircularPrism(includeAnalyticProvenance: true);
        instanceGeometry.ExactMappedInstances =
        [
            new GeoInstance
            {
                SymbolId = symbolId,
                Transform = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0],
            },
        ];
        var symbol = new Element
        {
            Id = symbolId,
            IsBlockOnly = true,
            Geometry = symbolGeometry,
        };
        var instance = new Element
        {
            Id = 202,
            TypeId = symbolId,
            IsDiscovered = true,
            CategoryId = -2000340,
            Category = "OST_GenericModel",
            Name = "Mapped analytic cylinder",
            References = [3],
            Parameters =
            [
                new Parameter
                {
                    Id = -1012100,
                    StorageType = StorageType.ElementId,
                    Value = 3,
                },
            ],
            BoundingBox = instanceGeometry.BoundingBox,
            Geometry = instanceGeometry,
        };
        var info = new RevitFileInfo();
        info.Elements.Add(symbol);
        info.Elements.Add(instance);
        string path = Path.Combine(Path.GetTempPath(),
            $"revitreader-mapped-cylinder-{Guid.NewGuid():N}.ifc");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.Contains("=IFCBUILDINGELEMENTPROXY(", ifc);
            Assert.Single(Regex.Matches(ifc, "=IFCREPRESENTATIONMAP\\("));
            Assert.Single(Regex.Matches(ifc, "=IFCMAPPEDITEM\\("));
            Assert.Single(Regex.Matches(ifc, "=IFCCIRCLEPROFILEDEF\\("));
            Assert.Single(Regex.Matches(ifc, "=IFCEXTRUDEDAREASOLID\\("));
            Assert.Contains("'SweptSolid'", ifc);
            Assert.DoesNotContain("=IFCFACETEDBREP(", ifc);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static GeometryData CircularPrism(bool includeAnalyticProvenance)
    {
        const int segmentCount = 24;
        const double radius = 0.25;
        double[] origin = [0, 0, 0.5];
        double[] axis = [0, 1, 0];
        double[] referenceDirection = [1, 0, 0];
        double[] secondRadialDirection = [0, 0, -1];
        var lower = new List<double[]>(segmentCount);
        var upper = new List<double[]>(segmentCount);
        for (int index = 0; index < segmentCount; index++)
        {
            double angle = 2 * Math.PI * index / segmentCount;
            double radialX = radius * Math.Cos(angle);
            double radialY = radius * Math.Sin(angle);
            double[] radial =
            [
                radialX * referenceDirection[0] + radialY * secondRadialDirection[0],
                radialX * referenceDirection[1] + radialY * secondRadialDirection[1],
                radialX * referenceDirection[2] + radialY * secondRadialDirection[2],
            ];
            lower.Add([
                origin[0] + radial[0] - 2 * axis[0],
                origin[1] + radial[1] - 2 * axis[1],
                origin[2] + radial[2] - 2 * axis[2],
            ]);
            upper.Add([
                origin[0] + radial[0] + 2 * axis[0],
                origin[1] + radial[1] + 2 * axis[1],
                origin[2] + radial[2] + 2 * axis[2],
            ]);
        }

        var faces = new List<GeoFace>();
        for (int index = 0; index < segmentCount; index++)
        {
            int next = (index + 1) % segmentCount;
            var face = new GeoFace
            {
                OuterVertices =
                [lower[index], lower[next], upper[next], upper[index]],
            };
            if (includeAnalyticProvenance)
            {
                face.AnalyticCylinderOrigin = (double[])origin.Clone();
                face.AnalyticCylinderAxis = (double[])axis.Clone();
                face.AnalyticCylinderReferenceDirection =
                    (double[])referenceDirection.Clone();
                face.AnalyticCylinderRadius = radius;
            }
            faces.Add(face);
        }
        faces.Add(new GeoFace
        {
            OuterVertices = lower.AsEnumerable().Reverse().ToList(),
        });
        faces.Add(new GeoFace
        {
            OuterVertices = upper,
        });

        return new GeometryData
        {
            BoundingBox = [-radius, -2, 0.5 - radius, radius, 2, 0.5 + radius],
            Faces = faces,
            BrepSolids =
            [
                new GeoBrepSolid
                {
                    IsClosed = true,
                    FaceIndices = Enumerable.Range(0, faces.Count).ToList(),
                },
            ],
        };
    }
}
