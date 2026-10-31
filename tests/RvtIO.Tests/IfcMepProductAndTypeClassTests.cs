using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Product and type classes of the MEP categories as the current profile writes them: pipe fittings (IfcFlowFitting, IfcPipeFittingType),
/// pipe accessories (IfcFlowController, IfcValveType), IfcPipeSegmentType (one FLEXIBLESEGMENT), pipe insulations (IfcCovering, INSULATION,
/// no type at all), cable trays (IfcCableCarrierSegmentType, CABLETRAYSEGMENT) and their fittings (IfcCableCarrierFittingType).</summary>
public class IfcMepProductAndTypeClassTests
{
    [Theory]
    [InlineData(-2008044, "IFCFLOWSEGMENT")]    // OST_PipeCurves
    [InlineData(-2008050, "IFCFLOWSEGMENT")]    // OST_FlexPipeCurves
    [InlineData(-2008049, "IFCFLOWFITTING")]    // OST_PipeFitting
    [InlineData(-2008055, "IFCFLOWCONTROLLER")] // OST_PipeAccessory
    [InlineData(-2008122, "IFCCOVERING")]       // OST_PipeInsulations
    [InlineData(-2008130, "IFCFLOWSEGMENT")]    // OST_CableTray
    [InlineData(-2008099, "IFCFLOWTERMINAL")]   // OST_Sprinklers
    [InlineData(-2001043, "IFCFLOWTERMINAL")]   // OST_FoodServiceEquipment
    [InlineData(-2001036, "IFCBUILDINGELEMENTPROXY")] // OST_Hardscape
    [InlineData(-2008126, "IFCFLOWFITTING")]    // OST_CableTrayFitting
    public void Category_MapsToTheMeasuredProductClass(int category, string expected)
    {
        var method = typeof(IfcExporter).GetMethod("MapCategoryToIfcType", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [category]));
    }

    [Theory]
    [InlineData("IFCFLOWSEGMENT", -2008044, "IFCPIPESEGMENTTYPE", "NOTDEFINED")]
    [InlineData("IFCFLOWSEGMENT", -2008050, "IFCPIPESEGMENTTYPE", "FLEXIBLESEGMENT")]
    [InlineData("IFCFLOWSEGMENT", -2008130, "IFCCABLECARRIERSEGMENTTYPE", "CABLETRAYSEGMENT")]
    [InlineData("IFCFLOWFITTING", -2008049, "IFCPIPEFITTINGTYPE", "NOTDEFINED")]
    [InlineData("IFCFLOWFITTING", -2008126, "IFCCABLECARRIERFITTINGTYPE", "NOTDEFINED")]
    // The current profile over MEP sample projects of several Revit versions (2022 to 2026): hvac, electrical and plumbing.
    [InlineData("IFCFLOWFITTING", -2008128, "IFCCABLECARRIERFITTINGTYPE", "NOTDEFINED")]
    [InlineData("IFCFLOWFITTING", -2008010, "IFCDUCTFITTINGTYPE", "NOTDEFINED")]
    [InlineData("IFCFLOWSEGMENT", -2008020, "IFCDUCTSEGMENTTYPE", "FLEXIBLESEGMENT")]
    [InlineData("IFCFLOWSEGMENT", -2008132, "IFCCABLECARRIERSEGMENTTYPE", "CONDUITSEGMENT")]
    [InlineData("IFCFLOWTERMINAL", -2008013, "IFCAIRTERMINALTYPE", "NOTDEFINED")]
    [InlineData("IFCFLOWTERMINAL", -2008083, "IFCELECTRICAPPLIANCETYPE", "NOTDEFINED")]
    [InlineData("IFCFLOWTERMINAL", -2008099, "IFCFIRESUPPRESSIONTERMINALTYPE", "NOTDEFINED")]
    [InlineData("IFCFLOWCONTROLLER", -2008055, "IFCVALVETYPE", "NOTDEFINED")]
    public void TypeClassAndPredefinedType_FollowTheCanonicalTypeCategory(
        string product, int category, string typeClass, string predefined)
    {
        string text = WriteTypes(product, category, category);
        int type = Id(text, typeClass);
        string[] args = Args(Line(text, type));
        Assert.Equal(10, args.Length);
        Assert.Equal("'Shared type'", args[2]);
        Assert.Equal("'500'", args[7]);
        Assert.Equal("$", args[8]);
        Assert.Equal($".{predefined}.", args[9]);
        Assert.Contains($"(#1001,#1002),#{type});", text);
        Assert.Single(Regex.Matches(text, "=IFCRELDEFINESBYTYPE\\("));
        Assert.DoesNotContain("=IFCDISTRIBUTIONELEMENTTYPE(", text);
    }

    [Theory]
    [InlineData("IFCFLOWFITTING", null)]
    [InlineData("IFCFLOWFITTING", -2001060)] // OST_ElectricalFixtures
    [InlineData("IFCFLOWCONTROLLER", null)]
    [InlineData("IFCFLOWCONTROLLER", -2008016)] // OST_DuctAccessory
    public void FittingOrControllerOfAnotherCategory_GetsNoType(string product, int? typeCategory)
    {
        string text = WriteTypes(product, typeCategory, typeCategory);
        Assert.DoesNotContain("TYPE(", text);
        Assert.DoesNotContain("=IFCRELDEFINESBYTYPE(", text);
    }

    [Theory]
    [InlineData(-2008122)]
    [InlineData(null)]
    public void PipeInsulation_GetsNoCoveringType(int? occurrenceCategory)
    {
        // The category of the occurrence alone decides: no IfcCoveringType is written for insulation.
        string text = WriteTypes("IFCCOVERING", -2008122, occurrenceCategory);
        if (occurrenceCategory == -2008122)
            Assert.DoesNotContain("=IFCCOVERINGTYPE(", text);
        else
            Assert.Contains("=IFCCOVERINGTYPE(", text);
        Assert.Equal(occurrenceCategory == -2008122 ? 0 : 1, Regex.Matches(text, "=IFCRELDEFINESBYTYPE\\(").Count);
    }

    [Fact]
    public void Ceiling_KeepsItsCoveringType()
    {
        string text = WriteTypes("IFCCOVERING", -2000038, -2000038);
        Assert.EndsWith(",.CEILING.);", Line(text, Id(text, "IFCCOVERINGTYPE")));
    }

    [Theory]
    [InlineData("IFCFLOWSEGMENT", -2008044, -2008130)]
    [InlineData("IFCFLOWSEGMENT", -2008130, -2008044)]
    [InlineData("IFCFLOWSEGMENT", -2008050, -2008044)]
    public void OccurrenceCategoryDisagreeingWithTheType_KeepsTheExplicitUnknown(
        string product, int typeCategory, int occurrenceCategory)
    {
        string text = WriteTypes(product, typeCategory, occurrenceCategory);
        Assert.Matches(@"=IFC(PIPE|CABLECARRIER)SEGMENTTYPE\(", text);
        Assert.EndsWith(",.NOTDEFINED.);", Line(text, Id(text, typeCategory == -2008130 ? "IFCCABLECARRIERSEGMENTTYPE" : "IFCPIPESEGMENTTYPE")));
    }

    [Theory]
    [InlineData("IFCFLOWFITTING", -2008049, "IFCPIPEFITTINGTYPE")]
    [InlineData("IFCFLOWFITTING", -2008126, "IFCCABLECARRIERFITTINGTYPE")]
    [InlineData("IFCFLOWCONTROLLER", -2008055, "IFCVALVETYPE")]
    [InlineData("IFCFLOWSEGMENT", -2008044, "IFCPIPESEGMENTTYPE")]
    public void DistinctGeometrySymbols_SplitTheMepTypes(string product, int category, string typeClass)
    {
        string split = WriteMappedTypes(product, category, 601, 602);
        Assert.Equal(2, Regex.Matches(split, $"={typeClass}\\(").Count);
        string shared = WriteMappedTypes(product, category, 601, 601);
        int id = Id(shared, typeClass);
        Assert.Contains($"(#1001,#1002),#{id});", shared);
    }

    [Theory]
    [InlineData("IFCFLOWTERMINAL", false)]
    [InlineData("IFCFLOWSEGMENT", false)]
    [InlineData("IFCFLOWFITTING", false)]
    [InlineData("IFCFLOWCONTROLLER", false)]
    [InlineData("IFCFLOWTERMINAL", true)]
    [InlineData("IFCFLOWSEGMENT", true)]
    [InlineData("IFCFLOWFITTING", true)]
    [InlineData("IFCFLOWCONTROLLER", true)]
    [InlineData("IFCBUILDINGELEMENTPROXY", true)]
    [InlineData("IFCDISTRIBUTIONFLOWELEMENT", true)]
    public void DistributionCommonGate_FittingsAndControllersJoinOnlyInTheNewerRelease(string ifcType, bool currentProfile)
    {
        bool expected = ifcType is "IFCFLOWTERMINAL" or "IFCFLOWSEGMENT"
            || currentProfile && ifcType is "IFCFLOWFITTING" or "IFCFLOWCONTROLLER";
        Assert.Equal(expected, IfcExporter.ReceivesDistributionFlowElementCommonPropertySet(ifcType, currentProfile));
    }

    [Theory]
    [InlineData("IFCFLOWFITTING", "Baseline", false, "Baseline")]
    [InlineData("IFCFLOWFITTING", "Baseline", true, "Own")]
    [InlineData("IFCFLOWCONTROLLER", "Baseline", false, "Baseline")]
    [InlineData("IFCFLOWCONTROLLER", "Baseline", true, "Own")]
    public void OwnSymbolReference_ReachesFittingsAndControllersOnlyInTheNewerRelease(
        string cls, string baseline, bool currentProfile, string expected)
    {
        var element = new Element { ValidatedDistributionTypeReferenceName = "Own" };
        Assert.Equal(expected, IfcExporter.ResolveQuantityTakeOffReferenceValue(element, cls, baseline, currentProfile));
    }

    [Fact]
    public void FlowController_IsAnIfcElementProduct() =>
        Assert.True(IfcExporter.IsIfcElementSubclassProduct("IFCFLOWCONTROLLER"));

    [Theory]
    [InlineData(-2008049, "IFCFLOWFITTING", true)]
    [InlineData(-2008055, "IFCFLOWCONTROLLER", true)]
    [InlineData(-2008126, "IFCFLOWFITTING", true)]
    [InlineData(-2008049, "IFCFLOWFITTING", false)]
    [InlineData(-2008055, "IFCFLOWCONTROLLER", false)]
    public void WholeIfc_WritesTheProductWithItsTagAndTheCommonSetOfTheRelease(int category, string cls, bool currentProfile)
    {
        var info = new RevitFileInfo();
        info.Elements.Add(IfcExporterCoveringCommonPropertySetTests.Product(100, 200, category, "OST_TestMep", "Type name"));
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-mep-class-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = currentProfile ? IfcProfile.Current : IfcProfile.Legacy }.Export(info, path);
            string ifc = File.ReadAllText(path);
            var product = Assert.Single(Regex.Matches(ifc, $@"#\d+={cls}\(([^\r\n]*)\);"));
            Assert.Equal(8, Args(product.Groups[1].Value).Length);
            Assert.EndsWith(",'100'", product.Groups[1].Value);
            Assert.Single(Regex.Matches(ifc, "'Pset_QuantityTakeOff'"));
            Assert.Equal(currentProfile ? 1 : 0, Regex.Matches(ifc, "'Pset_DistributionFlowElementCommon'").Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(false, "INSULATION")]
    [InlineData(true, "NOTDEFINED")]
    public void WholeIfc_WritesPipeInsulationAsAnInsulationCovering(bool overridden, string expected)
    {
        var info = new RevitFileInfo();
        var insulation = IfcExporterCoveringCommonPropertySetTests.Product(
            100, 200, -2008122, "OST_PipeInsulations", "Insulation type");
        if (overridden) insulation.Parameters.Add(new Parameter
        {
            Definition = new Definition { Name = "IfcExportType" }, StorageType = StorageType.String, Value = "USERDEFINED",
        });
        info.Elements.Add(insulation);
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-insulation-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter().Export(info, path);
            string ifc = File.ReadAllText(path);
            var product = Assert.Single(Regex.Matches(ifc, @"#\d+=IFCCOVERING\(([^\r\n]*)\);"));
            Assert.EndsWith($",'100',.{expected}.", product.Groups[1].Value);
            Assert.DoesNotContain("IFCBUILDINGELEMENTPROXY(", ifc);
            Assert.Contains("'Pset_CoveringCommon'", ifc);
            Assert.DoesNotContain("IFCCOVERINGTYPE(", ifc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(-2008122, "INSULATION")]
    [InlineData(-2000038, "CEILING")]
    [InlineData(-2000032, "NOTDEFINED")]
    [InlineData(-2008055, "NOTDEFINED")]
    public void InsulationDefault_UsesOnlyTheIntrinsicCategory(int category, string expected)
    {
        var covering = new Element { Id = 100, CategoryId = category };
        Assert.Equal(expected, IfcExporter.ResolveCoveringOccurrencePredefinedType(covering, null, Counts()));
    }

    [Theory]
    [InlineData("duplicate_occurrence")] [InlineData("block_only")] [InlineData("wrong_type_category")]
    [InlineData("duplicate_type")] [InlineData("override_on_type")]
    public void InsulationAmbiguity_DoesNotBecomeACategoryDefault(string mutation)
    {
        var covering = new Element { Id = 100, TypeId = 200, CategoryId = -2008122 };
        var type = new Element { Id = 200, CategoryId = -2008122 };
        var counts = Counts();
        switch (mutation)
        {
            case "duplicate_occurrence": counts[100] = 2; break;
            case "block_only": covering.IsBlockOnly = true; break;
            case "wrong_type_category": type.CategoryId = -2000038; break;
            case "duplicate_type": counts[200] = 2; break;
            case "override_on_type":
                type.Parameters.Add(new Parameter
                {
                    Definition = new Definition { Name = "IfcExportAs[Type]" }, StorageType = StorageType.String, Value = "IfcCovering.USERDEFINED",
                });
                break;
        }
        Assert.Equal("NOTDEFINED", IfcExporter.ResolveCoveringOccurrencePredefinedType(covering, type, counts));
    }

    [Theory]
    [InlineData(-2008044, true)]
    [InlineData(-2008050, true)]
    [InlineData(-2008130, true)]
    [InlineData(-2008000, false)] // duct segment types: no GUID source
    public void SystemSegmentType_TakesTheExportIdOfTheTypeElement(int category, bool expected)
    {
        var exportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");
        Element Instance(int id) => new()
        {
            Id = id, CategoryId = category, NativeMasterSymbolId = 500,
            Parameters = [new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = 500,
            }],
        };
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Shared type", CategoryId = category, SerializedExportId = exportId },
            [1] = Instance(1), [2] = Instance(2),
        };
        var (text, sources) = InvokeWithGuidSources(elements, "IFCFLOWSEGMENT");
        if (expected)
            Assert.Equal(IfcGuid.ToIfcGuid(exportId), Assert.Single(sources).Value);
        else
            Assert.Empty(sources);
        Assert.Single(Regex.Matches(text, "=IFCRELDEFINESBYTYPE\\("));
    }

    [Theory]
    [InlineData("IFCFLOWFITTING", -2008049, "IFCPIPEFITTINGTYPE")]
    [InlineData("IFCFLOWCONTROLLER", -2008055, "IFCVALVETYPE")]
    public void SameSymbolInTwoPipingSystemTypes_GivesTwoTypesAndTheSymbolGuidToTheFirst(
        string product, int category, string typeClass)
    {
        var symbolExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");
        var (text, sources) = InvokeWithGuidSources(
            MappedWithSystemTypes(category, symbolExportId, 700, 701), product);
        var types = text.Split('\n').Where(line => Regex.IsMatch(line, $@"^#\d+={typeClass}\(")).ToArray();
        Assert.Equal(2, types.Length);
        Assert.Equal(2, types.Select(line => Args(line)[0]).Distinct().Count());
        // The instance with the lowest element id belongs to the type that keeps the symbol's ExportId.
        int keeper = Assert.Single(sources).Key;
        Assert.Equal(IfcGuid.ToIfcGuid(symbolExportId), sources[keeper]);
        Assert.Contains($"(#1001),#{keeper});", text);
        Assert.DoesNotContain($"(#1002),#{keeper});", text);
    }

    [Fact]
    public void SameSymbolAndSystemType_KeepsOneType()
    {
        var symbolExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");
        var (text, sources) = InvokeWithGuidSources(
            MappedWithSystemTypes(-2008049, symbolExportId, 700, 700, 701), "IFCFLOWFITTING");
        Assert.Equal(2, Regex.Matches(text, "=IFCPIPEFITTINGTYPE\\(").Count);
        Assert.Contains("(#1001,#1002),#", text);
        Assert.Contains("(#1003),#", text);
        Assert.Single(sources);
    }

    [Theory]
    [InlineData(-1)] // several systems: the parameter is -1
    [InlineData(0)]
    public void AnInstanceWithoutASystemType_IsNotMergedIntoASystemTypeGroup(int missing)
    {
        var elements = MappedWithSystemTypes(-2008049, null, 700, missing);
        if (missing == 0) elements[2].Parameters.RemoveAll(parameter => parameter.Id == -1140334);
        string text = InvokeWithGuidSources(elements, "IFCFLOWFITTING").Text;
        Assert.Equal(2, Regex.Matches(text, "=IFCPIPEFITTINGTYPE\\(").Count);
    }

    [Fact]
    public void SystemTypeOfAnotherCategory_DoesNotSplitTheType()
    {
        string text = InvokeWithGuidSources(MappedWithSystemTypes(-2008126, null, 700, 701), "IFCFLOWFITTING").Text;
        Assert.Single(Regex.Matches(text, "=IFCCABLECARRIERFITTINGTYPE\\("));
    }

    [Theory]
    [InlineData(-2008049, -1140334, 700, 700)]
    [InlineData(-2008055, -1140334, 700, 700)]
    [InlineData(-2001160, -1140334, 700, 700)]
    [InlineData(-2008010, -1140333, 700, 700)]
    [InlineData(-2008013, -1140333, 700, 700)]
    [InlineData(-2008049, -1140333, 700, 0)]  // a duct parameter on a pipe category is ignored
    [InlineData(-2008010, -1140334, 700, 0)]
    [InlineData(-2008126, -1140334, 700, 0)]  // cable tray fitting: not a pipe or duct category
    [InlineData(-2008049, -1140334, -1, 0)]
    [InlineData(-2008049, -1140334, 0, 0)]
    public void SystemTypeKey_ReadsTheParameterOfTheCategoryDomain(int category, int parameterId, int value, int expected)
    {
        var instance = new Element { Id = 1, Parameters = [new Parameter { Id = parameterId, StorageType = StorageType.ElementId, Value = value }] };
        Assert.Equal(expected, IfcExporter.ResolveTypeKeySystemTypeId(instance, new Element { Id = 500, CategoryId = category }));
    }

    [Theory]
    [InlineData(-1, 712975, 712975)]   // a duct in no system (parameter -1) still stores its system type
    [InlineData(712974, 712975, 712975)]
    [InlineData(712974, null, 712974)]
    [InlineData(-1, null, 0)]
    [InlineData(-1, -1, 0)]
    public void SystemTypeKey_OfADuctIsTheSystemTypeItStoresItself(int parameter, int? stored, int expected)
    {
        var instance = new Element { Id = 1, SerializedCurveSystemTypeId = stored,
            Parameters = [new Parameter { Id = -1140333, StorageType = StorageType.ElementId, Value = parameter }] };
        Assert.Equal(expected, IfcExporter.ResolveTypeKeySystemTypeId(instance, new Element { Id = 500, CategoryId = -2008000 }));
    }

    private static Dictionary<int, Element> MappedWithSystemTypes(int category, Guid? symbolExportId, params int[] systemTypes)
    {
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Shared type", CategoryId = category },
            [601] = new() { Id = 601, Name = "Symbol", SerializedExportId = symbolExportId },
        };
        for (int i = 0; i < systemTypes.Length; i++)
            elements[i + 1] = new Element
            {
                Id = i + 1, CategoryId = category, NativeMasterSymbolId = 500,
                Parameters =
                [
                    new Parameter
                    {
                        Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                        StorageType = StorageType.ElementId, Value = 500,
                    },
                    new Parameter { Id = -1140334, StorageType = StorageType.ElementId, Value = systemTypes[i] },
                ],
                Geometry = new GeometryData { ExactMappedInstances = [new GeoInstance { SymbolId = 601 }] },
            };
        return elements;
    }

    private static Dictionary<int, int> Counts() => new() { [100] = 1, [200] = 1 };

    private static string WriteTypes(string product, int? typeCategory, int? occurrenceCategory)
    {
        Element Instance(int id) => new()
        {
            Id = id, CategoryId = occurrenceCategory, NativeMasterSymbolId = 500,
            Parameters = [new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = 500,
            }],
        };
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Shared type", CategoryId = typeCategory },
            [1] = Instance(1), [2] = Instance(2),
        };
        return Invoke(elements, product);
    }

    private static string WriteMappedTypes(string product, int category, int firstSymbol, int secondSymbol)
    {
        Element Instance(int id, int symbol) => new()
        {
            Id = id, CategoryId = category, NativeMasterSymbolId = 500,
            Parameters = [new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = 500,
            }],
            Geometry = new GeometryData { ExactMappedInstances = [new GeoInstance { SymbolId = symbol }] },
        };
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Shared type", CategoryId = category },
            [1] = Instance(1, firstSymbol), [2] = Instance(2, secondSymbol),
            [601] = new() { Id = 601, Name = "Symbol" }, [602] = new() { Id = 602, Name = "Another symbol" },
        };
        return Invoke(elements, product);
    }

    private static string Invoke(Dictionary<int, Element> elements, string product) =>
        InvokeWithGuidSources(elements, product).Text;

    private static (string Text, Dictionary<int, string> GuidSources) InvokeWithGuidSources(
        Dictionary<int, Element> elements, string product)
    {
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)>();
        foreach (int id in new[] { 1, 2, 3 })
            if (elements.ContainsKey(id)) products.Add(id, (1000 + id, 0, product));
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByType",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var sources = Assert.IsType<Dictionary<int, string>>(method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>()]));
        using var text = new StringWriter();
        writer.WriteTo(text, "test.ifc", "IFC2X3", "test", "test");
        return (text.ToString(), sources);
    }

    // Arguments of a whole entity line ("#12=IFCX(a,b);") or of an already extracted argument list.
    private static string[] Args(string text)
    {
        text = text.TrimEnd('\r');
        if (text.StartsWith('#')) text = text[(text.IndexOf('(') + 1)..^2];
        return text.Split(',');
    }

    private static int Id(string text, string cls) => int.Parse(
        Assert.Single(Regex.Matches(text, $@"#(\d+)={cls}\(")).Groups[1].Value);

    private static string Line(string text, int id) =>
        Assert.Single(text.Split('\n'), line => line.StartsWith($"#{id}=")).TrimEnd('\r');
}
