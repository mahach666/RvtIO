using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Occurrence-only Pset_DoorCommon: a successfully emitted, tagged IFCDOOR with a QTO Reference mirrors that property entity and adds the type-cached IsExternal. Door types, windows, pure cutters and products without a resolved QTO Reference are out of scope.</summary>
public class IfcExporterDoorCommonPropertySetTests
{
    [Fact]
    public void EmittedDoor_ReusesQtoReferencePropertyEntity_AndAddsIsExternal()
    {
        Element door = HostedProduct(
            id: 3000, typeId: 3001, categoryId: -2000023,
            name: "Door type");
        door.Parameters.Add(new Parameter
        {
            Id = 7000,
            Definition = new Definition { Id = 7000, Name = "is external" },
            StorageType = StorageType.Integer,
            Value = 1,
        });

        string ifc = ExportToText(door);

        int doorId = SoleEntityId(ifc, "IFCDOOR");
        Pset qto = SolePset(ifc, "Pset_QuantityTakeOff");
        Pset common = SolePset(ifc, "Pset_DoorCommon");
        Dictionary<string, PropertyValue> properties = Properties(ifc, common);

        Assert.Equal(doorId, SolePsetTarget(ifc, qto.Id));
        Assert.Equal(doorId, SolePsetTarget(ifc, common.Id));
        Assert.Equal(2, common.ValueIds.Count);
        Assert.Equal(Assert.Single(qto.ValueIds), properties["Reference"].Id);
        Assert.Equal("IFCBOOLEAN(.F.)", properties["IsExternal"].Expression);
        Assert.Equal("IFCIDENTIFIER('Door type')", properties["Reference"].Expression);
        Assert.Equal(new[] { "IsExternal", "Reference" }, properties.Keys.OrderBy(key => key));
        Assert.Single(Regex.Matches(
            ifc,
            @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('Door type'\)"));
        Assert.Contains(
            $"IFCPROPERTYSET('{IfcGuid.FromName("door-common-pset:3000")}',",
            ifc);
        Assert.Contains(
            $"IFCRELDEFINESBYPROPERTIES('{IfcGuid.FromName("door-common-rel:3000")}',",
            ifc);
        Assert.DoesNotContain("IFCDOORSTYLE", ifc);
        Assert.DoesNotContain("IFCDOORTYPE", ifc);
    }

    [Fact]
    public void SameReferenceAcrossDoors_SharesPropertyButKeepsDistinctPsetsAndRelations()
    {
        string ifc = ExportToText(
            HostedProduct(3010, 3012, -2000023, "Shared door type", centreX: 0),
            HostedProduct(3011, 3012, -2000023, "Shared door type", centreX: 10));

        int[] doorIds = Regex.Matches(ifc, @"#(\d+)=IFCDOOR\(")
            .Select(match => int.Parse(match.Groups[1].Value))
            .OrderBy(id => id).ToArray();
        List<Pset> qtos = Psets(ifc, "Pset_QuantityTakeOff");
        List<Pset> commons = Psets(ifc, "Pset_DoorCommon");

        Assert.Equal(2, doorIds.Length);
        Assert.Equal(2, qtos.Count);
        Assert.Equal(2, commons.Count);
        int referenceId = Assert.Single(
            qtos.SelectMany(pset => pset.ValueIds).Distinct());
        Assert.All(commons, common =>
            Assert.Contains(referenceId, common.ValueIds));
        Assert.Equal(
            doorIds,
            qtos.Select(pset => SolePsetTarget(ifc, pset.Id))
                .OrderBy(id => id).ToArray());
        Assert.Equal(
            doorIds,
            commons.Select(pset => SolePsetTarget(ifc, pset.Id))
                .OrderBy(id => id).ToArray());
        Assert.Single(Regex.Matches(
            ifc,
            @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('Shared door type'\)"));
        Assert.Contains(IfcGuid.FromName("door-common-pset:3010"), ifc);
        Assert.Contains(IfcGuid.FromName("door-common-pset:3011"), ifc);
        Assert.Contains(IfcGuid.FromName("door-common-rel:3010"), ifc);
        Assert.Contains(IfcGuid.FromName("door-common-rel:3011"), ifc);

        List<string> globalIds = Regex.Matches(
                ifc,
                @"#\d+=(?:IFCPROPERTYSET|IFCRELDEFINESBYPROPERTIES)\('([^']+)'\)")
            .Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(globalIds.Count, globalIds.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrBlockedQtoReference_CreatesNoOrphanDoorCommon(bool blocked)
    {
        Element door = HostedProduct(
            id: 3020, typeId: 3021, categoryId: -2000023,
            name: blocked ? "Fallback Reference" : null);
        if (blocked)
        {
            door.Parameters.Add(new Parameter
            {
                Id = 7020,
                Definition = new Definition { Id = 7020, Name = "Reference" },
                StorageType = StorageType.Double,
                Value = 12.5,
            });
        }

        string ifc = ExportToText(door);

        Assert.True(SoleEntityId(ifc, "IFCDOOR") > 0);
        Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
        Assert.DoesNotContain("Pset_DoorCommon", ifc);
        Assert.DoesNotContain("IFCIDENTIFIER('Fallback Reference')", ifc);
        Assert.DoesNotContain("door-common-pset", ifc);
        Assert.DoesNotContain("door-common-rel", ifc);
    }

    [Fact]
    public void ExplicitReference_FlowsThroughExistingResolverIntoBothPsets()
    {
        Element door = HostedProduct(
            id: 3030, typeId: 3031, categoryId: -2000023,
            name: "Baseline door type");
        door.Parameters.Add(new Parameter
        {
            Id = 7030,
            Definition = new Definition { Id = 7030, Name = "Re fer ence" },
            StorageType = StorageType.String,
            Value = "Authored door reference",
        });

        string ifc = ExportToText(door);
        Pset qto = SolePset(ifc, "Pset_QuantityTakeOff");
        Pset common = SolePset(ifc, "Pset_DoorCommon");

        Assert.Contains(Assert.Single(qto.ValueIds), common.ValueIds);
        Assert.Equal(
            "IFCIDENTIFIER('Authored door reference')",
            Properties(ifc, common)["Reference"].Expression);
        Assert.DoesNotContain("IFCIDENTIFIER('Baseline door type')", ifc);
    }

    [Fact]
    public void EmittedWindow_KeepsQtoButReceivesNoDoorCommon()
    {
        string ifc = ExportToText(HostedProduct(
            id: 3040, typeId: 3041, categoryId: -2000014,
            name: "Window type"));

        Assert.True(SoleEntityId(ifc, "IFCWINDOW") > 0);
        Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"));
        Assert.DoesNotContain("Pset_DoorCommon", ifc);
        Assert.DoesNotContain("door-common-pset", ifc);
        Assert.DoesNotContain("door-common-rel", ifc);
    }

    [Fact]
    public void PureDoorCutterWithoutVisibleBody_CreatesNoProductOrOccurrencePset()
    {
        Element cutter = HostedProduct(
            id: 3050, typeId: 3051, categoryId: -2000023,
            name: "Pure door cutter");
        cutter.Geometry = null;

        string ifc = ExportToText(cutter);

        Assert.DoesNotContain("=IFCDOOR(", ifc);
        Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
        Assert.DoesNotContain("Pset_DoorCommon", ifc);
        Assert.DoesNotContain("door-common-pset", ifc);
        Assert.DoesNotContain("door-common-rel", ifc);
    }

    private sealed record Pset(int Id, IReadOnlyList<int> ValueIds);

    private sealed record PropertyValue(int Id, string Expression);

    private static Dictionary<string, PropertyValue> Properties(string ifc, Pset pset)
    {
        var result = new Dictionary<string, PropertyValue>();
        foreach (int valueId in pset.ValueIds)
        {
            Match match = Assert.Single(Regex.Matches(
                ifc,
                $@"#{valueId}=IFCPROPERTYSINGLEVALUE\('((?:''|[^'])*)',(?:\$|''),(.*),\$\);"));
            result.Add(
                match.Groups[1].Value.Replace("''", "'"),
                new PropertyValue(valueId, match.Groups[2].Value));
        }
        return result;
    }

    private static Pset SolePset(string ifc, string psetName) =>
        Assert.Single(Psets(ifc, psetName));

    private static List<Pset> Psets(string ifc, string psetName) =>
        Regex.Matches(
                ifc,
                $@"#(\d+)=IFCPROPERTYSET\('[^']*',#\d+,'{Regex.Escape(psetName)}',(?:\$|''),\((#[0-9]+(?:,#[0-9]+)*)\)\);")
            .Select(match => new Pset(
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Value.Split(',')
                    .Select(value => int.Parse(value[1..])).ToList()))
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

    private static Element HostedProduct(
        int id, int typeId, int categoryId, string? name, double centreX = 0) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            SuperInstanceId = 999,
            IsDiscovered = true,
            CategoryId = categoryId,
            Category = categoryId == -2000023 ? "OST_Doors" : "OST_Windows",
            Name = name,
            FamilyName = "Hosted family",
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
            $"revitreader-door-common-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter() { Profile = IfcProfile.Legacy }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
