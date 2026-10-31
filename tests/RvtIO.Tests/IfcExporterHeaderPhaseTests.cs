using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>A placed instance is told from a preview or type record by its project phase: the serialized
/// Element.m_createdPhaseId (header) or, failing that, a reference to the phase in its record graph.</summary>
public class IfcExporterHeaderPhaseTests
{
    [Fact]
    public void Floor_WithTheHeaderPhaseButNoRecordReferences_IsExported() =>
        Assert.Contains("=IFCSLAB(", Export(Floor(references: [], headerPhase: 3)));

    [Fact]
    public void Floor_WithTheReferenceButNoHeaderPhase_IsStillExported() =>
        Assert.Contains("=IFCSLAB(", Export(Floor(references: [3], headerPhase: null)));

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(4)]
    public void Floor_WithoutAnyValidationOfTheProjectPhase_IsNoProduct(int? headerPhase) =>
        Assert.DoesNotContain("=IFCSLAB(", Export(Floor(references: [], headerPhase: headerPhase)));

    private static Element Floor(List<int> references, int? headerPhase)
    {
        double[][] points = [[0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0], [0, 0, 1], [4, 0, 1], [4, 3, 1], [0, 3, 1]];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        return new Element
        {
            Id = 100, TypeId = 500, CategoryId = -2000032, Category = "OST_Floors", Name = "Floor",
            References = references, SerializedCreatedPhaseId = headerPhase,
            Parameters = [new Parameter { Id = -1002050, Value = 500, StorageType = StorageType.ElementId,
                    Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" } },
                new Parameter { Id = -1012100, Value = 3, StorageType = StorageType.ElementId }],
            BoundingBox = [0, 0, 0, 4, 3, 1],
            Geometry = new GeometryData { BoundingBox = [0, 0, 0, 4, 3, 1],
                Faces = faces.Select(indices => new GeoFace { OuterVertices = indices.Select(index => points[index]).ToList() }).ToList(),
                BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] }] },
        };
    }

    private static string Export(Element floor)
    {
        var type = new Element { Id = 500, TypeId = -1, CategoryId = -2000032, Name = "Floor type" };
        var info = new RevitFileInfo(); info.Elements.AddRange([floor, type]);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-header-phase-{Guid.NewGuid():N}.ifc");
        try { new IfcExporter() { Profile = IfcProfile.Legacy }.Export(info, path); return File.ReadAllText(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
