using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Occurrence-only Pset_ReinforcementBarPitchOfBeam.Reference: every tagged IFCBEAM/QTO carrier receives a private IFCLABEL property with the QTO scalar text; the QTO IFCIDENTIFIER itself is never reused.</summary>
public class IfcExporterReinforcementBarPitchOfBeamPropertySetTests
{
    private const string PitchPsetName = "Pset_ReinforcementBarPitchOfBeam";

    [Fact]
    public void SampleGroup_ThirteenBeams_GetPrivateIfcLabelValuesAndDeterministicAttachments()
    {
        Element[] beams = Enumerable.Range(0, 13)
            .Select(index => Product(
                id: 6100 + index,
                typeId: 6200,
                categoryId: -2001320,
                category: "OST_StructuralFraming",
                name: "Shared beam reference",
                centreX: index * 10))
            .ToArray();

        string ifc = ExportToText(beams);

        int[] beamIds = EntityIds(ifc, "IFCBEAM").OrderBy(id => id).ToArray();
        List<Pset> qtos = Psets(ifc, "Pset_QuantityTakeOff");
        List<Pset> commons = Psets(ifc, "Pset_BeamCommon");
        List<Pset> pitches = Psets(ifc, PitchPsetName);

        Assert.Equal(13, beamIds.Length);
        Assert.Equal(13, qtos.Count);
        Assert.Equal(13, commons.Count);
        Assert.Equal(13, pitches.Count);

        int qtoReferenceId = Assert.Single(
            qtos.Select(pset => Assert.Single(pset.ValueIds)).Distinct());
        Assert.Equal(
            "IFCIDENTIFIER('Shared beam reference')",
            PropertyExpression(ifc, qtoReferenceId));

        int[] pitchValueIds = pitches
            .Select(pset => Assert.Single(pset.ValueIds)).ToArray();
        Assert.Equal(13, pitchValueIds.Distinct().Count());
        Assert.DoesNotContain(qtoReferenceId, pitchValueIds);
        Assert.All(pitchValueIds, valueId =>
            Assert.Equal(
                "IFCLABEL('Shared beam reference')",
                PropertyExpression(ifc, valueId)));

        Assert.Equal(
            beamIds,
            pitches.Select(pset => SolePsetTarget(ifc, pset.Id))
                .OrderBy(id => id).ToArray());
        foreach (Element beam in beams)
        {
            Assert.Contains(
                $"IFCPROPERTYSET('{IfcGuid.FromName($"reinforcement-bar-pitch-beam-pset:{beam.Id}")}',",
                ifc);
            Assert.Contains(
                $"IFCRELDEFINESBYPROPERTIES('{IfcGuid.FromName($"reinforcement-bar-pitch-beam-rel:{beam.Id}")}',",
                ifc);
        }

        Assert.Single(Regex.Matches(
            ifc,
            @"=IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('Shared beam reference'\),\$\);"));
        Assert.Equal(13, Regex.Matches(
            ifc,
            @"=IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCLABEL\('Shared beam reference'\),\$\);").Count);
        Assert.Equal(16, Regex.Matches(ifc, "=IFCPROPERTYSINGLEVALUE\\(").Count);
        // 13 beams x 3 psets (QTO + BeamCommon + pitch) + 1 Pset_BuildingCommon.
        Assert.Equal(40, Regex.Matches(ifc, "=IFCPROPERTYSET\\(").Count);
        Assert.Equal(40, Regex.Matches(ifc, "=IFCRELDEFINESBYPROPERTIES\\(").Count);
        Assert.DoesNotContain("IFCBEAMTYPE", ifc);
    }

    [Fact]
    public void AuthoredReference_FlowsIntoIdentifierQtoAndPrivateLabelPitch()
    {
        Element beam = Product(
            id: 6300, typeId: 6301, categoryId: -2001320,
            category: "OST_StructuralFraming", name: "Fallback beam reference");
        beam.Parameters.Add(new Parameter
        {
            Id = 7300,
            Definition = new Definition { Id = 7300, Name = "Re fer ence" },
            StorageType = StorageType.String,
            Value = "Authored beam reference",
        });

        string ifc = ExportToText(beam);
        Pset qto = Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"));
        Pset pitch = Assert.Single(Psets(ifc, PitchPsetName));
        int qtoValueId = Assert.Single(qto.ValueIds);
        int pitchValueId = Assert.Single(pitch.ValueIds);

        Assert.NotEqual(qtoValueId, pitchValueId);
        Assert.Equal(
            "IFCIDENTIFIER('Authored beam reference')",
            PropertyExpression(ifc, qtoValueId));
        Assert.Equal(
            "IFCLABEL('Authored beam reference')",
            PropertyExpression(ifc, pitchValueId));
        Assert.DoesNotContain("IFCIDENTIFIER('Fallback beam reference')", ifc);
        Assert.DoesNotContain("IFCLABEL('Fallback beam reference')", ifc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrBlockedQtoReference_CreatesNoOrphanBeamPitch(bool blocked)
    {
        Element beam = Product(
            id: 6400, typeId: 6401, categoryId: -2001320,
            category: "OST_StructuralFraming",
            name: blocked ? "Fallback beam reference" : null);
        if (blocked)
        {
            beam.Parameters.Add(new Parameter
            {
                Id = 7400,
                Definition = new Definition { Id = 7400, Name = "Reference" },
                StorageType = StorageType.Double,
                Value = 12.5,
            });
        }

        string ifc = ExportToText(beam);

        Assert.Single(EntityIds(ifc, "IFCBEAM"));
        Assert.Empty(Psets(ifc, "Pset_QuantityTakeOff"));
        Assert.Empty(Psets(ifc, PitchPsetName));
        Assert.DoesNotContain("reinforcement-bar-pitch-beam-pset", ifc);
        Assert.DoesNotContain("reinforcement-bar-pitch-beam-rel", ifc);
        Assert.DoesNotContain("IFCLABEL('Fallback beam reference')", ifc);
    }

    [Fact]
    public void GeometrylessBeam_CreatesNoProductQtoOrBeamPitch()
    {
        Element beam = Product(
            id: 6500, typeId: 6501, categoryId: -2001320,
            category: "OST_StructuralFraming", name: "Suppressed beam");
        beam.Geometry = null;

        string ifc = ExportToText(beam);

        Assert.Empty(EntityIds(ifc, "IFCBEAM"));
        Assert.Empty(Psets(ifc, "Pset_QuantityTakeOff"));
        Assert.Empty(Psets(ifc, PitchPsetName));
        Assert.DoesNotContain("reinforcement-bar-pitch-beam-pset", ifc);
        Assert.DoesNotContain("reinforcement-bar-pitch-beam-rel", ifc);
    }

    [Fact]
    public void SampleStyleModelWithoutBeam_IsExactEmptyNonBeamControl()
    {
        string ifc = ExportToText(
            Product(6600, 6610, -2001330, "OST_StructuralColumns", "Column", 0),
            Product(6601, 6611, -2000126, "OST_StairsRailing", "Railing", 10),
            Product(6602, 6612, -2000014, "OST_Windows", "Window", 20));

        Assert.Empty(EntityIds(ifc, "IFCBEAM"));
        Assert.Equal(3, Psets(ifc, "Pset_QuantityTakeOff").Count);
        Assert.Empty(Psets(ifc, PitchPsetName));
        Assert.DoesNotContain("reinforcement-bar-pitch-beam-pset", ifc);
        Assert.DoesNotContain("reinforcement-bar-pitch-beam-rel", ifc);
        Assert.DoesNotContain("Pset_ReinforcementBarPitchOfBeam", ifc);
    }

    private sealed record Pset(int Id, IReadOnlyList<int> ValueIds);

    private static List<Pset> Psets(string ifc, string psetName) =>
        Regex.Matches(
                ifc,
                $@"#(\d+)=IFCPROPERTYSET\('[^']*',#\d+,'{Regex.Escape(psetName)}',(?:\$|''),\((#[0-9]+(?:,#[0-9]+)*)\)\);")
            .Select(match => new Pset(
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Value.Split(',')
                    .Select(value => int.Parse(value[1..])).ToList()))
            .ToList();

    private static string PropertyExpression(string ifc, int valueId)
    {
        Match match = Assert.Single(Regex.Matches(
            ifc,
            $@"#{valueId}=IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),(.*),\$\);"));
        return match.Groups[1].Value;
    }

    private static int SolePsetTarget(string ifc, int psetId)
    {
        Match match = Assert.Single(Regex.Matches(
            ifc,
            $@"IFCRELDEFINESBYPROPERTIES\('[^']*',#\d+,(?:\$|''),(?:\$|''),\(#(\d+)\),#{psetId}\);"));
        return int.Parse(match.Groups[1].Value);
    }

    private static List<int> EntityIds(string ifc, string ifcType) =>
        Regex.Matches(ifc, $@"#(\d+)={ifcType}\(")
            .Select(match => int.Parse(match.Groups[1].Value)).ToList();

    private static Element Product(
        int id, int typeId, int categoryId, string category, string? name,
        double centreX = 0) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            SuperInstanceId = 999,
            IsDiscovered = true,
            CategoryId = categoryId,
            Category = category,
            Name = name,
            FamilyName = "Beam-pitch test family",
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
            BoundingBox = [centreX, 0, 0, centreX + 4, 3, 2],
            Geometry = SolidPrism(centreX),
        };

    private static GeometryData SolidPrism(double centreX)
    {
        double[][] points =
        [
            [centreX, 0, 0], [centreX + 4, 0, 0],
            [centreX + 4, 3, 0], [centreX, 3, 0],
            [centreX, 0, 2], [centreX + 4, 0, 2],
            [centreX + 4, 3, 2], [centreX, 3, 2],
        ];
        int[][] faces =
        [
            [0, 3, 2, 1], [4, 5, 6, 7],
            [0, 1, 5, 4], [1, 2, 6, 5],
            [2, 3, 7, 6], [3, 0, 4, 7],
        ];
        return new GeometryData
        {
            BoundingBox = [centreX, 0, 0, centreX + 4, 3, 2],
            Faces = faces.Select(indices => new GeoFace
            {
                OuterVertices = indices.Select(index => points[index]).ToList(),
            }).ToList(),
            BrepSolids =
            [
                new GeoBrepSolid
                {
                    IsClosed = true,
                    FaceIndices = [0, 1, 2, 3, 4, 5],
                },
            ],
        };
    }

    private static string ExportToText(params Element[] elements)
    {
        var info = new RevitFileInfo();
        info.Elements.AddRange(elements);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"revitreader-beam-pitch-{Guid.NewGuid():N}.ifc");
        try
        {
            // The legacy profile contract: the current profile also writes IsExternal into the beam common set.
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
