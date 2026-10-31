using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Pset_ReinforcementBarPitchOfColumn.Reference: every successfully tagged IFCCOLUMN/QTO carrier receives one private IFCLABEL property with the same text; no other product class and no QTO-less column participates.</summary>
public class IfcExporterReinforcementBarPitchOfColumnPropertySetTests
{
    [Fact]
    public void Column_UsesExactIfcLabelLiteral_OnItsOwnAttachment()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Product(
            id: 1200, typeId: 1201, categoryId: -2001330,
            name: "Column type 'A'"));
        string path = TempIfcPath("column-pitch-exact");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);

            int columnId = SoleEntityId(ifc, "IFCCOLUMN");
            var qto = SolePset(ifc, "Pset_QuantityTakeOff");
            var pitch = SolePset(ifc, "Pset_ReinforcementBarPitchOfColumn");

            Assert.Equal(columnId, SolePsetTarget(ifc, pitch.PsetId));
            Assert.Equal(columnId, SolePsetTarget(ifc, qto.PsetId));
            Assert.NotEqual(qto.ValueId, pitch.ValueId);
            Assert.Equal("Column type 'A'", LabelReferenceLiteral(ifc, pitch.ValueId));
            Assert.Equal("Column type 'A'", IdentifierReferenceLiteral(ifc, qto.ValueId));
            Assert.Contains(
                $"IFCPROPERTYSET('{IfcGuid.FromName("reinforcement-bar-pitch-column-pset:1200")}',",
                ifc);
            Assert.Contains(
                $"IFCRELDEFINESBYPROPERTIES('{IfcGuid.FromName("reinforcement-bar-pitch-column-rel:1200")}',",
                ifc);
            Assert.Equal(4, Regex.Matches(ifc, "=IFCPROPERTYSET\\(").Count);
            Assert.Equal(4, Regex.Matches(ifc, "=IFCRELDEFINESBYPROPERTIES\\(").Count);
            Assert.Equal(5, Regex.Matches(ifc, "=IFCPROPERTYSINGLEVALUE\\(").Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void SameLiteralOnTwoColumns_CreatesDistinctPrivateLabelProperties()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Product(
            id: 1210, typeId: 1212, categoryId: -2001330,
            name: "Shared column type", centreX: 0));
        info.Elements.Add(Product(
            id: 1211, typeId: 1212, categoryId: -2001330,
            name: "Shared column type", centreX: 10));
        string path = TempIfcPath("column-pitch-private-values");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);

            var qtoPsets = Psets(ifc, "Pset_QuantityTakeOff");
            var pitchPsets = Psets(ifc, "Pset_ReinforcementBarPitchOfColumn");
            Assert.Equal(2, qtoPsets.Count);
            Assert.Equal(2, pitchPsets.Count);
            Assert.Single(qtoPsets.Select(pset => pset.ValueId).Distinct());
            Assert.Equal(2, pitchPsets.Select(pset => pset.ValueId).Distinct().Count());
            Assert.DoesNotContain(
                qtoPsets[0].ValueId,
                pitchPsets.Select(pset => pset.ValueId));
            Assert.All(pitchPsets, pset =>
                Assert.Equal("Shared column type",
                    LabelReferenceLiteral(ifc, pset.ValueId)));
            Assert.Single(Regex.Matches(
                ifc,
                @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('Shared column type'\),\$\)"));
            Assert.Equal(2, Regex.Matches(
                ifc,
                @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCLABEL\('Shared column type'\),\$\)").Count);
            Assert.Equal(7, Regex.Matches(ifc, "=IFCPROPERTYSET\\(").Count);
            Assert.Equal(7, Regex.Matches(ifc, "=IFCRELDEFINESBYPROPERTIES\\(").Count);
            Assert.Equal(6, Regex.Matches(ifc, "=IFCPROPERTYSINGLEVALUE\\(").Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void NonColumn_KeepsQtoButReceivesNoColumnPitchPset()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Product(
            id: 1220, typeId: 1221, categoryId: -2001320,
            name: "Beam type"));
        string path = TempIfcPath("column-pitch-non-column");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.True(SoleEntityId(ifc, "IFCBEAM") > 0);
            Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"));
            Assert.DoesNotContain("Pset_ReinforcementBarPitchOfColumn", ifc);
            Assert.DoesNotContain(
                IfcGuid.FromName("reinforcement-bar-pitch-column-pset:1220"), ifc);
            Assert.DoesNotContain(
                IfcGuid.FromName("reinforcement-bar-pitch-column-rel:1220"), ifc);
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrBlockedQto_CreatesNoOrphanColumnPitchPset(bool blocked)
    {
        Element column = Product(
            id: 1230, typeId: 1231, categoryId: -2001330,
            name: blocked ? "Fallback column type" : null);
        if (blocked)
        {
            column.Parameters.Add(new Parameter
            {
                Id = 7300,
                Definition = new Definition { Id = 7300, Name = "Reference" },
                StorageType = StorageType.Double,
                Value = 12.5,
            });
        }
        var info = new RevitFileInfo();
        info.Elements.Add(column);
        string path = TempIfcPath("column-pitch-null-blocked");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.True(SoleEntityId(ifc, "IFCCOLUMN") > 0);
            Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
            Assert.DoesNotContain("Pset_ReinforcementBarPitchOfColumn", ifc);
            Assert.DoesNotContain("IFCIDENTIFIER('Fallback column type')", ifc);
            Assert.DoesNotContain("IFCLABEL('Fallback column type')", ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void AuthoredReference_FlowsAsTheSameEscapedPayloadIntoBothIfcValueTypes()
    {
        Element column = Product(
            id: 1240, typeId: 1241, categoryId: -2001330,
            name: "Baseline type");
        column.Parameters.Add(new Parameter
        {
            Id = 7400,
            Definition = new Definition { Id = 7400, Name = "Re fer ence" },
            StorageType = StorageType.String,
            Value = "Authored 'column' reference",
        });
        var info = new RevitFileInfo();
        info.Elements.Add(column);
        string path = TempIfcPath("column-pitch-authored-reference");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);
            var qto = SolePset(ifc, "Pset_QuantityTakeOff");
            var pitch = SolePset(ifc, "Pset_ReinforcementBarPitchOfColumn");

            Assert.Equal("Authored 'column' reference",
                IdentifierReferenceLiteral(ifc, qto.ValueId));
            Assert.Equal("Authored 'column' reference",
                LabelReferenceLiteral(ifc, pitch.ValueId));
            Assert.DoesNotContain("IFCIDENTIFIER('Baseline type')", ifc);
            Assert.DoesNotContain("IFCLABEL('Baseline type')", ifc);
        }
        finally { Cleanup(path); }
    }

    private static Element Product(
        int id, int typeId, int categoryId, string? name, double centreX = 0) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            IsDiscovered = true,
            CategoryId = categoryId,
            Category = categoryId == -2001330
                ? "OST_StructuralColumns"
                : "OST_StructuralFraming",
            Name = name,
            FamilyName = "Test family",
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

    private static (int PsetId, int ValueId) SolePset(string ifc, string psetName) =>
        Assert.Single(Psets(ifc, psetName));

    private static List<(int PsetId, int ValueId)> Psets(
        string ifc, string psetName) =>
        Regex.Matches(
                ifc,
                $@"#(\d+)=IFCPROPERTYSET\('[^']*',#\d+,'{Regex.Escape(psetName)}',(?:\$|''),\(#(\d+)\)\);")
            .Select(match => (
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value)))
            .ToList();

    private static int SolePsetTarget(string ifc, int psetId)
    {
        Match match = Assert.Single(Regex.Matches(
            ifc,
            $@"IFCRELDEFINESBYPROPERTIES\('[^']*',#\d+,(?:\$|''),(?:\$|''),\(#(\d+)\),#{psetId}\);"));
        return int.Parse(match.Groups[1].Value);
    }

    private static int SoleEntityId(string ifc, string ifcType)
    {
        Match match = Assert.Single(Regex.Matches(ifc, $@"#(\d+)={ifcType}\("));
        return int.Parse(match.Groups[1].Value);
    }

    private static string IdentifierReferenceLiteral(string ifc, int valueId) =>
        ReferenceLiteral(ifc, valueId, "IFCIDENTIFIER");

    private static string LabelReferenceLiteral(string ifc, int valueId) =>
        ReferenceLiteral(ifc, valueId, "IFCLABEL");

    private static string ReferenceLiteral(
        string ifc, int valueId, string ifcValueType)
    {
        Match match = Assert.Single(Regex.Matches(
            ifc,
            $@"#{valueId}=IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),{ifcValueType}\('((?:''|[^'])*)'\),\$\);"));
        return match.Groups[1].Value.Replace("''", "'");
    }

    private static string TempIfcPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"revitreader-{tag}-{Guid.NewGuid():N}.ifc");

    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
