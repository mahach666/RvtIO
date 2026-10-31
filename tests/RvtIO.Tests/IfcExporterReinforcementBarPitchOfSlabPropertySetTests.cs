using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Pset_ReinforcementBarPitchOfSlab.Reference: reached only after the same IFCSLAB product has emitted Pset_QuantityTakeOff.Reference; its text equals that Reference, but it is a private IFCLABEL property (the QTO one is an IFCIDENTIFIER) written directly and never entered in the shared value pool.</summary>
public class IfcExporterReinforcementBarPitchOfSlabPropertySetTests
{
    private const string PitchPsetName = "Pset_ReinforcementBarPitchOfSlab";
    private const int FloorCategoryId = -2000032;

    [Fact]
    public void Slab_UsesExactIfcLabelLiteral_OnItsOwnAttachment()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Slab(id: 900, typeId: 901, name: "Floor type 'A'"));
        string path = TempIfcPath("slab-pitch-exact");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            int slabId = SoleEntityId(ifc, "IFCSLAB");
            var qto = SolePset(ifc, "Pset_QuantityTakeOff");
            var pitch = SolePset(ifc, PitchPsetName);

            Assert.Equal(slabId, SolePsetTarget(ifc, pitch.PsetId));
            Assert.Equal(slabId, SolePsetTarget(ifc, qto.PsetId));
            Assert.NotEqual(qto.ValueId, pitch.ValueId);
            Assert.Equal("Floor type 'A'", LabelReferenceLiteral(ifc, pitch.ValueId));
            Assert.Equal("Floor type 'A'", IdentifierReferenceLiteral(ifc, qto.ValueId));
            Assert.Contains(
                $"IFCPROPERTYSET('{IfcGuid.FromName("reinforcement-bar-pitch-slab-pset:900")}',",
                ifc);
            Assert.Contains(
                $"IFCRELDEFINESBYPROPERTIES('{IfcGuid.FromName("reinforcement-bar-pitch-slab-rel:900")}',",
                ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void SameLiteralOnTwoSlabs_CreatesDistinctPrivateLabelProperties()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Slab(id: 910, typeId: 912, name: "Shared slab type", offsetX: 0));
        info.Elements.Add(Slab(id: 911, typeId: 912, name: "Shared slab type", offsetX: 20));
        string path = TempIfcPath("slab-pitch-private-values");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);

            var qtoPsets = Psets(ifc, "Pset_QuantityTakeOff");
            var pitchPsets = Psets(ifc, PitchPsetName);
            Assert.Equal(2, qtoPsets.Count);
            Assert.Equal(2, pitchPsets.Count);
            Assert.Single(qtoPsets.Select(p => p.ValueId).Distinct());
            Assert.Equal(2, pitchPsets.Select(p => p.ValueId).Distinct().Count());
            Assert.DoesNotContain(qtoPsets[0].ValueId, pitchPsets.Select(p => p.ValueId));
            Assert.Single(Regex.Matches(
                ifc,
                @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('Shared slab type'\),\$\)"));
            Assert.Equal(2, Regex.Matches(
                ifc,
                @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCLABEL\('Shared slab type'\),\$\)").Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void NamelessSlab_GetsNoQtoAndNoSlabPitch()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Slab(id: 920, typeId: 921, name: null));
        string path = TempIfcPath("slab-pitch-nameless");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
            Assert.DoesNotContain(PitchPsetName, ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void NonSlab_KeepsQtoButReceivesNoSlabPitch()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(Product(
            id: 930, typeId: 931, categoryId: -2001330, category: "OST_StructuralColumns",
            name: "Column type"));
        string path = TempIfcPath("slab-pitch-non-slab");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"));
            Assert.Empty(Psets(ifc, PitchPsetName));
            Assert.DoesNotContain(PitchPsetName, ifc);
        }
        finally { Cleanup(path); }
    }

    private static Element Slab(int id, int typeId, string? name, double offsetX = 0) => new()
    {
        Id = id,
        TypeId = typeId,
        IsDiscovered = true,
        CategoryId = FloorCategoryId,
        Category = "OST_Floors",
        Name = name,
        References = [3],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
        ],
        BoundingBox = [offsetX, 0, 0, offsetX + 4, 3, 0.3],
        Geometry = SolidPrism(offsetX),
    };

    private static Element Product(
        int id, int typeId, int categoryId, string category, string? name,
        double offsetX = 0) => new()
    {
        Id = id,
        TypeId = typeId,
        IsDiscovered = true,
        CategoryId = categoryId,
        Category = category,
        Name = name,
        References = [3],
        Parameters =
        [
            new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 },
        ],
        BoundingBox = [offsetX, 0, 0, offsetX + 4, 3, 2],
        Geometry = SolidPrism(offsetX, height: 2),
    };

    private static GeometryData SolidPrism(double offsetX, double height = 0.3)
    {
        double[][] points =
        [
            [offsetX, 0, 0], [offsetX + 4, 0, 0],
            [offsetX + 4, 3, 0], [offsetX, 3, 0],
            [offsetX, 0, height], [offsetX + 4, 0, height],
            [offsetX + 4, 3, height], [offsetX, 3, height],
        ];
        int[][] faces =
        [
            [0, 3, 2, 1], [4, 5, 6, 7],
            [0, 1, 5, 4], [1, 2, 6, 5],
            [2, 3, 7, 6], [3, 0, 4, 7],
        ];
        return new GeometryData
        {
            BoundingBox = [offsetX, 0, 0, offsetX + 4, 3, height],
            Faces = faces.Select(indices => new GeoFace
            {
                OuterVertices = indices.Select(index => points[index]).ToList(),
            }).ToList(),
            BrepSolids =
            [
                new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] },
            ],
        };
    }

    private static (int PsetId, int ValueId) SolePset(string ifc, string psetName) =>
        Assert.Single(Psets(ifc, psetName));

    private static List<(int PsetId, int ValueId)> Psets(string ifc, string psetName) =>
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

    private static string ReferenceLiteral(string ifc, int valueId, string ifcValueType)
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
