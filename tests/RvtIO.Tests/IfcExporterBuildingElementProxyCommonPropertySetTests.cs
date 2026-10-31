using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Exact occurrence-only Pset_BuildingElementProxyCommon.Reference group.
/// Every successfully emitted tagged proxy with QTO receives the Pset except an occurrence
/// named by a decoded floor CuttingElementIds roster through its exact direct serialized host
/// or one exact serialized intermediary, including an intermediary retained only in the raw
/// native-parameter graph.</summary>
public class IfcExporterBuildingElementProxyCommonPropertySetTests
{
    [Fact]
    public void DirectDecodedFloorRoster_ExcludesOnlyTheNamedOccurrence()
    {
        var proxy = new Element { Id = 300, SerializedHostId = 100 };
        var floor = new Element
        {
            Id = 100,
            CategoryId = -2000032,
            CuttingElementIds = [proxy.Id],
        };
        Dictionary<int, Element> elements = ById(proxy, floor);

        Assert.False(Receives(proxy, elements));

        floor.CuttingElementIds = [];
        Assert.True(Receives(proxy, elements));

        floor.CuttingElementIds = null;
        Assert.True(Receives(proxy, elements));

        floor.CuttingElementIds = [proxy.Id + 1];
        Assert.True(Receives(proxy, elements));
    }

    [Fact]
    public void DirectDecodedFloorWithNoHostSentinels_RemainsDirect()
    {
        var proxy = new Element
        {
            Id = 300,
            SerializedHostId = 100,
            SerializedHostParentId = -1,
        };
        var floor = new Element
        {
            Id = 100,
            CategoryId = -2000032,
            SerializedHostId = -1,
            CuttingElementIds = [proxy.Id],
        };

        Assert.False(Receives(proxy, ById(proxy, floor)));
    }

    [Fact]
    public void OneHopDecodedFloorRoster_ExcludesOnlyOnCompleteAcyclicValidation()
    {
        var proxy = new Element { Id = 300, SerializedHostId = 200 };
        var wrapper = new Element { Id = 200, SerializedHostId = 100 };
        var floor = new Element
        {
            Id = 100,
            CategoryId = -2000032,
            CuttingElementIds = [proxy.Id],
        };
        Dictionary<int, Element> elements = ById(proxy, wrapper, floor);

        Assert.False(Receives(proxy, elements));

        floor.CuttingElementIds = [];
        Assert.True(Receives(proxy, elements));

        floor.CuttingElementIds = [proxy.Id];
        wrapper.SerializedHostId = 999;
        Assert.True(Receives(proxy, elements));

        wrapper.SerializedHostId = wrapper.Id;
        Assert.True(Receives(proxy, elements));

        wrapper.SerializedHostId = proxy.Id;
        Assert.True(Receives(proxy, elements));

        wrapper.SerializedHostId = floor.Id;
        elements.Remove(wrapper.Id);
        Assert.True(Receives(proxy, elements));
    }

    [Fact]
    public void MissingSerializedIntermediary_UsesOnlyCarriedNativeHostParent()
    {
        var proxy = new Element
        {
            Id = 300,
            SerializedHostId = 200,
        };
        var floor = new Element
        {
            Id = 100,
            CategoryId = -2000032,
            CuttingElementIds = [proxy.Id],
        };
        var proxyParams = new TfBlockDeserializer.NativeParams();
        proxyParams.Fields["m_hostId"] = 200;
        var wrapperParams = new TfBlockDeserializer.NativeParams();
        wrapperParams.Fields["m_hostId"] = 100;

        ElementBuilder.ApplySerializedHostParentIds(
            [proxy, floor],
            new Dictionary<uint, TfBlockDeserializer.NativeParams>
            {
                [300] = proxyParams,
                [200] = wrapperParams,
            });

        Assert.Equal(100, proxy.SerializedHostParentId);
        Assert.False(Receives(proxy, ById(proxy, floor)));
    }

    [Fact]
    public void PostProcess_ReferencePropagatedFloorWrapper_RemainsSerializedIntermediary()
    {
        var proxy = new Element { Id = 300 };
        var wrapper = new Element { Id = 200, References = [100] };
        var floor = new Element
        {
            Id = 100,
            CategoryId = -2000032,
            CuttingElementIds = [proxy.Id],
        };
        var proxyParams = new TfBlockDeserializer.NativeParams();
        proxyParams.Fields["m_hostId"] = wrapper.Id;
        var wrapperParams = new TfBlockDeserializer.NativeParams();
        wrapperParams.Fields["m_hostId"] = floor.Id;
        var info = new RevitFileInfo { Elements = [proxy, wrapper, floor] };
        var state = new ParsingState();
        state.NativeParams[(uint)proxy.Id] = proxyParams;
        state.NativeParams[(uint)wrapper.Id] = wrapperParams;

        ElementBuilder.PostProcess(info, state);

        Assert.Equal(-2000032, wrapper.CategoryId);
        Assert.Equal(wrapper.Id, proxy.SerializedHostId);
        Assert.Equal(floor.Id, wrapper.SerializedHostId);
        Assert.Equal(floor.Id, proxy.SerializedHostParentId);
        Assert.False(Receives(proxy, ById(proxy, wrapper, floor)));
    }

    [Fact]
    public void CarriedNativeParent_RequiresAgreementWithMaterializedIntermediary()
    {
        var proxy = new Element
        {
            Id = 300,
            SerializedHostId = 200,
            SerializedHostParentId = 100,
        };
        var wrapper = new Element { Id = 200, SerializedHostId = 101 };
        var floor = new Element
        {
            Id = 100,
            CategoryId = -2000032,
            CuttingElementIds = [proxy.Id],
        };

        Assert.True(Receives(proxy, ById(proxy, wrapper, floor)));

        wrapper.SerializedHostId = 100;
        Assert.False(Receives(proxy, ById(proxy, wrapper, floor)));
    }

    [Fact]
    public void NativeHostParentCarry_RequiresOwnSerializedHostAgreement()
    {
        var proxy = new Element { Id = 300, SerializedHostId = 200 };
        var proxyParams = new TfBlockDeserializer.NativeParams();
        proxyParams.Fields["m_hostId"] = 201;
        var wrapperParams = new TfBlockDeserializer.NativeParams();
        wrapperParams.Fields["m_hostId"] = 100;

        ElementBuilder.ApplySerializedHostParentIds(
            [proxy],
            new Dictionary<uint, TfBlockDeserializer.NativeParams>
            {
                [300] = proxyParams,
                [200] = wrapperParams,
            });

        Assert.Null(proxy.SerializedHostParentId);
    }

    [Fact]
    public void NonFloorAndGenericHostSource_CannotExcludeProxyCommon()
    {
        var proxy = new Element
        {
            Id = 300,
            SerializedHostId = 999,
            HostId = 100,
            ExtraHostIds = [100],
            ExplicitHostIds = [100],
            References = [100],
        };
        var floor = new Element
        {
            Id = 100,
            CategoryId = -2000032,
            CuttingElementIds = [proxy.Id],
        };
        var wall = new Element
        {
            Id = 999,
            CategoryId = -2000011,
            CuttingElementIds = [proxy.Id],
        };

        Assert.True(Receives(proxy, ById(proxy, floor, wall)));
        Assert.False(IfcExporter.ReceivesBuildingElementProxyCommonPropertySet(
            "IFCBEAM", proxy, ById(proxy, floor, wall)));
    }

    [Fact]
    public void SameReferenceAcrossProxies_ReusesQtoPropertyAndKeepsDistinctAttachments()
    {
        string ifc = ExportToText(
            Proxy(3010, "Shared proxy type", 0),
            Proxy(3011, "Shared proxy type", 10));

        int[] proxyIds = EntityIds(ifc, "IFCBUILDINGELEMENTPROXY");
        List<Pset> qtos = Psets(ifc, "Pset_QuantityTakeOff");
        List<Pset> commons = Psets(ifc, "Pset_BuildingElementProxyCommon");

        Assert.Equal(2, proxyIds.Length);
        Assert.Equal(2, qtos.Count);
        Assert.Equal(2, commons.Count);
        int referenceId = Assert.Single(
            qtos.SelectMany(pset => pset.ValueIds).Distinct());
        Assert.All(commons, common =>
            Assert.Equal(referenceId, Assert.Single(common.ValueIds)));
        Assert.Equal(proxyIds,
            qtos.Select(pset => SolePsetTarget(ifc, pset.Id))
                .OrderBy(id => id).ToArray());
        Assert.Equal(proxyIds,
            commons.Select(pset => SolePsetTarget(ifc, pset.Id))
                .OrderBy(id => id).ToArray());
        Assert.Single(Regex.Matches(ifc,
            @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('Shared proxy type'\)"));
        Assert.Contains(
            IfcGuid.FromName("building-element-proxy-common-pset:3010"), ifc);
        Assert.Contains(
            IfcGuid.FromName("building-element-proxy-common-pset:3011"), ifc);
        Assert.Contains(
            IfcGuid.FromName("building-element-proxy-common-rel:3010"), ifc);
        Assert.Contains(
            IfcGuid.FromName("building-element-proxy-common-rel:3011"), ifc);
    }

    [Fact]
    public void ValidatedProxyTypeReference_OverridesWrongGeneralName()
    {
        // The general name fan-out runs before ApplyNativeParams canonicalizes TypeId from
        // m_masterSymbolId, so an earlier wrong TypeId guess can bake an unrelated type's name
        // into elem.Name. The proxy's own decoded m_masterSymbolId link (ValidatedProxyTypeReferenceName)
        // overrides that baseline for Reference and names the product after the same type.
        Element proxy = Proxy(3060, "Wrong sibling name", 0);
        proxy.ValidatedProxyTypeReferenceName = "Correct proxy type Reference";

        string ifc = ExportToText(proxy);

        Pset common = Assert.Single(Psets(ifc, "Pset_BuildingElementProxyCommon"));
        int referenceId = Assert.Single(common.ValueIds);
        Assert.Contains(
            $"#{referenceId}=IFCPROPERTYSINGLEVALUE('Reference','',IFCIDENTIFIER('Correct proxy type Reference'),$);",
            ifc);
        // revit-ifc's Name and ObjectType carry the type's name too.
        Assert.Contains("'Proxy family:Correct proxy type Reference:3060'", ifc);
        Assert.Equal("Wrong sibling name", proxy.Name);
    }

    [Fact]
    public void OwnWallSweepTypeReference_IsSharedByQtoAndCommonAndNamesTheProduct()
    {
        Element proxy = Proxy(3060, "Wrong mark", 0);
        proxy.CategoryId = -2000181; proxy.TypeId = 999;
        var type = new Element { Id = 500, CategoryId = -2000181, Name = "Wrong type mark" };
        var state = new ParsingState();
        state.WallSweepTypeIds[3060] = (1, 500);
        state.CorniceTypeNames[500] = (1, "Own cornice");
        ElementBuilder.ApplyValidatedWallSweepTypeReferenceNames([proxy,type], state);
        string ifc = ExportToText(proxy,type);
        var qto = Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"));
        var common = Assert.Single(Psets(ifc, "Pset_BuildingElementProxyCommon"));
        Assert.Equal(qto.ValueIds, common.ValueIds);
        int referenceId = Assert.Single(common.ValueIds);
        Assert.Contains($"#{referenceId}=IFCPROPERTYSINGLEVALUE('Reference','',IFCIDENTIFIER('Own cornice'),$);", ifc);
        Assert.Contains("'Proxy family:Own cornice:3060'", ifc);
        Assert.Equal(999, proxy.TypeId);
    }

    [Fact]
    public void SiblingWithoutValidatedProxyTypeReference_FallsBackToBaselineName()
    {
        // A proxy without its own decoded m_masterSymbolId link (ValidatedProxyTypeReferenceName null) must keep
        // the ordinary baseline Reference (elem.Name), not go bare or pick up another proxy's type name.
        Element sibling = Proxy(3061, "Existing proxy name", 0);
        Assert.Null(sibling.ValidatedProxyTypeReferenceName);

        string ifc = ExportToText(sibling);

        Pset common = Assert.Single(Psets(ifc, "Pset_BuildingElementProxyCommon"));
        int referenceId = Assert.Single(common.ValueIds);
        Assert.Contains(
            $"#{referenceId}=IFCPROPERTYSINGLEVALUE('Reference','',IFCIDENTIFIER('Existing proxy name'),$);",
            ifc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrBlockedQtoReference_CreatesNoOrphanProxyCommon(bool blocked)
    {
        Element proxy = Proxy(3020,
            blocked ? "Fallback proxy type" : null, 0);
        if (blocked)
        {
            proxy.Parameters.Add(new Parameter
            {
                Id = 7020,
                Definition = new Definition { Id = 7020, Name = "Reference" },
                StorageType = StorageType.Double,
                Value = 12.5,
            });
        }

        string ifc = ExportToText(proxy);

        Assert.Single(EntityIds(ifc, "IFCBUILDINGELEMENTPROXY"));
        Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
        Assert.DoesNotContain("Pset_BuildingElementProxyCommon", ifc);
        Assert.DoesNotContain("building-element-proxy-common-pset", ifc);
        Assert.DoesNotContain("building-element-proxy-common-rel", ifc);
    }

    [Fact]
    public void EmittedNonProxy_KeepsQtoButReceivesNoProxyCommon()
    {
        Element beam = Proxy(3030, "Beam type", 0);
        beam.CategoryId = -2001320;
        beam.Category = "OST_StructuralFraming";

        string ifc = ExportToText(beam);

        Assert.Single(EntityIds(ifc, "IFCBEAM"));
        Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"));
        Assert.Empty(Psets(ifc, "Pset_BuildingElementProxyCommon"));
    }

    [Fact]
    public void DirectDecodedSlabCutter_StillEmitsProductAndQtoButNoProxyCommon()
    {
        Element proxy = Proxy(3040, "Slab cutter proxy", 0);
        proxy.SerializedHostId = 100;
        var floor = new Element
        {
            Id = 100,
            IsBlockOnly = true,
            CategoryId = -2000032,
            Category = "OST_Floors",
            CuttingElementIds = [proxy.Id],
        };

        string ifc = ExportToText(floor, proxy);

        int proxyId = Assert.Single(EntityIds(ifc, "IFCBUILDINGELEMENTPROXY"));
        Pset qto = Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"));
        Assert.Equal(proxyId, SolePsetTarget(ifc, qto.Id));
        Assert.Empty(Psets(ifc, "Pset_BuildingElementProxyCommon"));
    }

    [Fact]
    public void WallCutterWithMaterializedOpening_RetainsProxyCommon()
    {
        (Element wall, Element cutter) = HostedWallCutter();

        string ifc = ExportToText(wall, cutter);

        int proxyId = Assert.Single(EntityIds(ifc, "IFCBUILDINGELEMENTPROXY"));
        Assert.Single(EntityIds(ifc, "IFCOPENINGELEMENT"));
        Assert.Single(Regex.Matches(ifc, "=IFCRELVOIDSELEMENT\\("));
        Pset common = Assert.Single(Psets(
            ifc, "Pset_BuildingElementProxyCommon"));
        Pset qto = Assert.Single(Psets(ifc, "Pset_QuantityTakeOff"),
            pset => SolePsetTarget(ifc, pset.Id) == proxyId);

        Assert.Equal(proxyId, SolePsetTarget(ifc, common.Id));
        Assert.Equal(Assert.Single(qto.ValueIds), Assert.Single(common.ValueIds));
        Assert.True(Receives(cutter, ById(wall, cutter)));
    }

    private sealed record Pset(int Id, IReadOnlyList<int> ValueIds);

    private static bool Receives(
        Element proxy, IReadOnlyDictionary<int, Element> elements) =>
        IfcExporter.ReceivesBuildingElementProxyCommonPropertySet(
            "IFCBUILDINGELEMENTPROXY", proxy, elements);

    private static Dictionary<int, Element> ById(params Element[] elements) =>
        elements.ToDictionary(element => element.Id);

    private static List<Pset> Psets(string ifc, string psetName) =>
        Regex.Matches(ifc,
                $@"#(\d+)=IFCPROPERTYSET\('[^']*',#\d+,'{Regex.Escape(psetName)}',(?:\$|''),\((#[0-9]+(?:,#[0-9]+)*)\)\);")
            .Select(match => new Pset(
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Value.Split(',')
                    .Select(value => int.Parse(value[1..])).ToList()))
            .ToList();

    private static int SolePsetTarget(string ifc, int psetId)
    {
        Match match = Assert.Single(Regex.Matches(ifc,
            $@"IFCRELDEFINESBYPROPERTIES\('[^']*',#\d+,(?:\$|''),(?:\$|''),\(#(\d+)\),#{psetId}\);"));
        return int.Parse(match.Groups[1].Value);
    }

    private static int[] EntityIds(string ifc, string ifcType) =>
        Regex.Matches(ifc, $@"#(\d+)={ifcType}\(")
            .Select(match => int.Parse(match.Groups[1].Value))
            .OrderBy(id => id).ToArray();

    private static Element Proxy(
        int id, string? name, double minimumX) => new()
        {
            Id = id,
            TypeId = 900,
            IsDiscovered = true,
            CategoryId = -2000151,
            Category = "OST_GenericModel",
            Name = name,
            FamilyName = "Proxy family",
            References = [3],
            Parameters = [PhaseParameter()],
            BoundingBox = [minimumX, 0, 0, minimumX + 4, 3, 2],
            Geometry = BoxGeometry(
                minimumX, 0, 0, minimumX + 4, 3, 2),
        };

    private static (Element Wall, Element Cutter) HostedWallCutter()
    {
        const int wallId = 100;
        const int cutterId = 3050;
        var wall = new Element
        {
            Id = wallId,
            TypeId = 101,
            CategoryId = -2000011,
            Category = "OST_Walls",
            Name = "Host wall",
            References = [3, cutterId],
            CuttingElementIds = [cutterId],
            Parameters =
            [
                PhaseParameter(),
                new Parameter
                {
                    Id = -1001000,
                    Definition = new Definition
                    {
                        Name = "WALL_ATTR_WIDTH_PARAM",
                    },
                    StorageType = StorageType.Double,
                    Value = 1.0,
                },
            ],
            Location = new LocationCurve
            {
                CurveType = "Line",
                StartPoint = [0, 0, 0],
                EndPoint = [4, 0, 0],
            },
            BoundingBox = [0, -0.5, 0, 4, 0.5, 3],
        };
        var cutter = new Element
        {
            Id = cutterId,
            TypeId = 3051,
            IsDiscovered = true,
            CategoryId = -2000151,
            Category = "OST_GenericModel",
            Name = "Wall cutter proxy",
            FamilyName = "Cutter family",
            SerializedHostId = wall.Id,
            HostId = wall.Id,
            References = [wall.Id, 3],
            Parameters = [PhaseParameter()],
            BoundingBox = [1, -0.6, 1, 2, 0.6, 2],
            Geometry = BoxGeometry(1, -0.6, 1, 2, 0.6, 2),
        };
        return (wall, cutter);
    }

    private static Parameter PhaseParameter() => new()
    {
        Id = -1012100,
        StorageType = StorageType.ElementId,
        Value = 3,
    };

    private static GeometryData BoxGeometry(
        double x0, double y0, double z0,
        double x1, double y1, double z1)
    {
        double[][] points =
        [
            [x0, y0, z0], [x1, y0, z0],
            [x1, y1, z0], [x0, y1, z0],
            [x0, y0, z1], [x1, y0, z1],
            [x1, y1, z1], [x0, y1, z1],
        ];
        int[][] faces =
        [
            [0, 3, 2, 1], [4, 5, 6, 7],
            [0, 1, 5, 4], [1, 2, 6, 5],
            [2, 3, 7, 6], [3, 0, 4, 7],
        ];
        return new GeometryData
        {
            BoundingBox = [x0, y0, z0, x1, y1, z1],
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
        using var output = new StringWriter();
        new IfcExporter().Export(info, output);
        return output.ToString();
    }
}
