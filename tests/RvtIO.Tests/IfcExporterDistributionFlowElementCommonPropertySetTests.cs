using RvtIO.Ifc;
using RvtIO.Model;
using System.Text.RegularExpressions;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Pset_DistributionFlowElementCommon.Reference: emitted IFCFLOWTERMINAL and IFCFLOWSEGMENT products mirror their already-emitted Pset_QuantityTakeOff Reference property entity; no other IFC class participates.</summary>
public class IfcExporterDistributionFlowElementCommonPropertySetTests
{
    [Fact]
    public void OwnSymbolName_IsSharedByBothSetsWithoutChangingProductIdentity()
    {
        var info = new RevitFileInfo();
        var element = FlowElement(900, 901, -2001160, "Old public name");
        element.ValidatedDistributionTypeReferenceName = "Own symbol name";
        info.Elements.Add(element);
        string path = TempIfcPath("distribution-own-symbol");
        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);
            var qto = SolePset(ifc, "Pset_QuantityTakeOff");
            var common = SolePset(ifc, "Pset_DistributionFlowElementCommon");
            Assert.Equal(qto.ValueId, common.ValueId);
            Assert.Equal("Own symbol name", ReferenceLiteral(ifc, common.ValueId));
            Assert.Equal("Old public name", element.Name);
            Assert.Equal(901, element.TypeId);
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("IFCFLOWTERMINAL", "Baseline", false, false, "Own")]
    [InlineData("IFCFLOWSEGMENT", "Baseline", false, false, "Own")]
    [InlineData("IFCFLOWFITTING", "Baseline", false, false, "Baseline")]
    [InlineData("IFCBUILDINGELEMENTPROXY", "Baseline", false, false, "Baseline")]
    [InlineData("IFCFLOWTERMINAL", null, false, false, null)]
    [InlineData("IFCFLOWTERMINAL", null, true, false, "Own")]
    [InlineData("IFCFLOWTERMINAL", "Baseline", false, true, null)]
    public void OwnSymbolReference_PreservesConsumptionAndPrecedenceGates(
        string cls, string? baseline, bool explicitValue, bool blocked, string? expected)
    {
        var element = new Element { ValidatedDistributionTypeReferenceName = "Own",
            ValidatedDistributionTypeReferenceIsExplicit = explicitValue,
            ValidatedDistributionTypeReferenceIsBlocked = blocked };
        Assert.Equal(expected, IfcExporter.ResolveQuantityTakeOffReferenceValue(element, cls, baseline));
    }

    [Theory]
    [InlineData(-2001160, "IFCFLOWTERMINAL")]
    [InlineData(-2008000, "IFCFLOWSEGMENT")]
    public void FlowTerminalAndSegment_ReuseTheirQtoReferencePropertyEntity(
        int categoryId, string ifcType)
    {
        var info = new RevitFileInfo();
        info.Elements.Add(FlowElement(
            id: 900, typeId: 901, categoryId, name: "Shared flow type"));
        string path = TempIfcPath("distribution-common-allowed");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            int productId = SoleEntityId(ifc, ifcType);
            var qto = SolePset(ifc, "Pset_QuantityTakeOff");
            var common = SolePset(ifc, "Pset_DistributionFlowElementCommon");

            Assert.Equal(productId, SolePsetTarget(ifc, qto.PsetId));
            Assert.Equal(productId, SolePsetTarget(ifc, common.PsetId));
            Assert.Equal(qto.ValueId, common.ValueId);
            Assert.Equal("Shared flow type", ReferenceLiteral(ifc, common.ValueId));
            Assert.Single(Regex.Matches(
                ifc,
                @"IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('Shared flow type'\)"));
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData("IFCFLOWTERMINAL", true)]
    [InlineData("IFCFLOWSEGMENT", true)]
    [InlineData("IFCFLOWFITTING", false)]
    [InlineData("IFCDISTRIBUTIONFLOWELEMENT", false)]
    [InlineData("IFCBUILDINGELEMENTPROXY", false)]
    [InlineData("IFCBEAM", false)]
    public void ClassGate_IsTheExactMeasuredAllowlist(string ifcType, bool expected)
    {
        Assert.Equal(expected,
            IfcExporter.ReceivesDistributionFlowElementCommonPropertySet(ifcType));
    }

    [Fact]
    public void FlowFitting_KeepsQtoButReceivesNoDistributionCommon_InLegacy()
    {
        var info = new RevitFileInfo();
        info.Elements.Add(FlowElement(
            id: 910, typeId: 911, categoryId: -2008049,
            name: "Fitting type"));
        string path = TempIfcPath("distribution-common-fitting-scope");

        try
        {
            new IfcExporter { Profile = IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.True(SoleEntityId(ifc, "IFCFLOWFITTING") > 0);
            Assert.Single(Regex.Matches(ifc, "'Pset_QuantityTakeOff'"));
            Assert.DoesNotContain("Pset_DistributionFlowElementCommon", ifc);
        }
        finally { Cleanup(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrBlockedQtoReference_CreatesNoOrphanDistributionCommon(bool blocked)
    {
        Element terminal = FlowElement(
            id: 920, typeId: 921, categoryId: -2001160,
            name: blocked ? "Fallback Reference" : null);
        if (blocked)
        {
            terminal.Parameters.Add(new Parameter
            {
                Id = 7001,
                Definition = new Definition { Id = 7001, Name = "Reference" },
                StorageType = StorageType.Double,
                Value = 12.5,
            });
        }
        var info = new RevitFileInfo();
        info.Elements.Add(terminal);
        string path = TempIfcPath("distribution-common-null-blocked");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            Assert.True(SoleEntityId(ifc, "IFCFLOWTERMINAL") > 0);
            Assert.DoesNotContain("Pset_QuantityTakeOff", ifc);
            Assert.DoesNotContain("Pset_DistributionFlowElementCommon", ifc);
            Assert.DoesNotContain("IFCIDENTIFIER('Fallback Reference')", ifc);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void ExplicitReference_FlowsThroughExistingResolverIntoBothPsets()
    {
        Element terminal = FlowElement(
            id: 930, typeId: 931, categoryId: -2001160,
            name: "Baseline type name");
        terminal.Parameters.Add(new Parameter
        {
            Id = 7002,
            Definition = new Definition { Id = 7002, Name = "Re fer ence" },
            StorageType = StorageType.String,
            Value = "Authored Reference",
        });
        var info = new RevitFileInfo();
        info.Elements.Add(terminal);
        string path = TempIfcPath("distribution-common-explicit-reference");

        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);

            var qto = SolePset(ifc, "Pset_QuantityTakeOff");
            var common = SolePset(ifc, "Pset_DistributionFlowElementCommon");
            Assert.Equal(qto.ValueId, common.ValueId);
            Assert.Equal("Authored Reference", ReferenceLiteral(ifc, qto.ValueId));
            Assert.DoesNotContain("IFCIDENTIFIER('Baseline type name')", ifc);
        }
        finally { Cleanup(path); }
    }

    private static Element FlowElement(
        int id, int typeId, int categoryId, string? name) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            IsDiscovered = true,
            CategoryId = categoryId,
            Category = "OST_TestFlow",
            Name = name,
            FamilyName = "Flow family",
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
            BoundingBox = [0, 0, 0, 4, 3, 2],
            Geometry = SolidPrism(),
        };

    private static GeometryData SolidPrism()
    {
        double[][] points =
        [
            [0, 0, 0], [4, 0, 0], [4, 3, 0], [0, 3, 0],
            [0, 0, 2], [4, 0, 2], [4, 3, 2], [0, 3, 2],
        ];
        int[][] faces =
        [
            [0, 3, 2, 1], [4, 5, 6, 7],
            [0, 1, 5, 4], [1, 2, 6, 5],
            [2, 3, 7, 6], [3, 0, 4, 7],
        ];
        return new GeometryData
        {
            BoundingBox = [0, 0, 0, 4, 3, 2],
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

    private static (int PsetId, int ValueId) SolePset(string ifc, string psetName)
    {
        Match match = Assert.Single(Regex.Matches(
            ifc,
            $@"#(\d+)=IFCPROPERTYSET\('[^']*',#\d+,'{Regex.Escape(psetName)}',(?:\$|''),\(#(\d+)\)\);"));
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
    }

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

    private static string ReferenceLiteral(string ifc, int valueId)
    {
        Match match = Assert.Single(Regex.Matches(
            ifc,
            $@"#{valueId}=IFCPROPERTYSINGLEVALUE\('Reference',(?:\$|''),IFCIDENTIFIER\('([^']*)'\),\$\);"));
        return match.Groups[1].Value;
    }

    private static string TempIfcPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"revitreader-{tag}-{Guid.NewGuid():N}.ifc");

    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
