using System.Reflection;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>TypeObjectKey of the current profile beyond the geometry symbol: the system type of pipe and duct segments
/// and an instance whose geometry holds several GInstances.</summary>
public class IfcTypeKeyCurrentTests
{
    private static readonly Guid TypeExportId = Guid.Parse("1f34752d-926d-4ab8-8425-c6d81fe02dab");
    private static readonly Guid FirstSymbolExportId = Guid.Parse("2a34752d-926d-4ab8-8425-c6d81fe02dab");
    private static readonly Guid SecondSymbolExportId = Guid.Parse("3b34752d-926d-4ab8-8425-c6d81fe02dab");

    [Theory]
    [InlineData(-2008044, -1140334, "IFCPIPESEGMENTTYPE")] // OST_PipeCurves
    [InlineData(-2008050, -1140334, "IFCPIPESEGMENTTYPE")] // OST_FlexPipeCurves
    [InlineData(-2008000, -1140333, "IFCDUCTSEGMENTTYPE")] // OST_DuctCurves
    public void SegmentsOfOneTypeInTwoSystemTypes_GiveTwoTypesInTheNewerRelease(int category, int parameterId, string typeClass)
    {
        var elements = Segments(category, parameterId, 700, 700, 701);
        string text = Write(elements, "IFCFLOWSEGMENT", currentProfile: true).Text;
        Assert.Equal(2, Regex.Matches(text, $"={typeClass}\\(").Count);
        Assert.Contains("(#1001,#1002),#", text);
        Assert.Contains("(#1003),#", text);
    }

    [Fact]
    public void SegmentsOfOneTypeInTwoSystemTypes_KeepOneTypeInTheOlderRelease()
    {
        string text = Write(Segments(-2008044, -1140334, 700, 701), "IFCFLOWSEGMENT", currentProfile: false).Text;
        Assert.Single(Regex.Matches(text, "=IFCPIPESEGMENTTYPE\\("));
        Assert.Contains("(#1001,#1002),#", text);
    }

    [Theory]
    [InlineData(0)]  // the parameter is absent
    [InlineData(-1)] // several systems
    public void SegmentWithoutASystemType_IsNotMergedIntoASystemTypeGroup(int missing)
    {
        var elements = Segments(-2008044, -1140334, 700, missing);
        if (missing == 0) elements[2].Parameters.RemoveAll(parameter => parameter.Id == -1140334);
        Assert.Equal(2, Regex.Matches(Write(elements, "IFCFLOWSEGMENT", currentProfile: true).Text, "=IFCPIPESEGMENTTYPE\\(").Count);
    }

    [Fact]
    public void CableTraySegments_AreNotSplitBySystemType()
    {
        var elements = Segments(-2008130, -1140334, 700, 701);
        Assert.Single(Regex.Matches(Write(elements, "IFCFLOWSEGMENT", currentProfile: true).Text, "=IFCCABLECARRIERSEGMENTTYPE\\("));
    }

    [Fact]
    public void SplitSegmentTypes_AllTakeTheExportIdOfTheTypeElement()
    {
        var (_, sources) = Write(Segments(-2008044, -1140334, 700, 701), "IFCFLOWSEGMENT", currentProfile: true);
        Assert.Equal(2, sources.Count);
        Assert.All(sources.Values, guid => Assert.Equal(IfcGuid.ToIfcGuid(TypeExportId), guid));
    }

    [Fact]
    public void ApplyTypeGlobalIds_GivesSplitSegmentTypesAndTheirRelationsTheSharedGlobalId()
    {
        var writer = new StepWriter();
        string guid = IfcGuid.ToIfcGuid(TypeExportId);
        var sources = new Dictionary<int, string>();
        for (int i = 0; i < 2; i++)
        {
            int type = writer.Add("IFCPIPESEGMENTTYPE",
                $"'{IfcGuid.FromName($"old-type-{i}")}',$,'Pipe',$,$,$,$,'500',$,.NOTDEFINED.");
            writer.Add("IFCRELDEFINESBYTYPE",
                $"'{IfcGuid.FromName($"old-rel-{i}")}',900,$,$,(#{1001 + i}),#{type}");
            sources[type] = guid;
        }
        Assert.Equal((2, 2), Apply(writer, sources));
        string relation = IfcGuid.FromMd5($"IfcRelDefinesByType:{guid}");
        var entities = Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToArray();
        Assert.Equal(2, entities.Count(entity => entity.Type == "IFCPIPESEGMENTTYPE" && entity.Args.StartsWith($"'{guid}'")));
        Assert.Equal(2, entities.Count(entity => entity.Type == "IFCRELDEFINESBYTYPE" && entity.Args.StartsWith($"'{relation}'")));
    }

    [Fact]
    public void ApplyTypeGlobalIds_StillRefusesAGlobalIdSharedByOtherTypes()
    {
        var writer = new StepWriter();
        string guid = IfcGuid.ToIfcGuid(TypeExportId);
        var sources = new Dictionary<int, string>();
        for (int i = 0; i < 2; i++)
        {
            int type = writer.Add("IFCMEMBERTYPE",
                $"'{IfcGuid.FromName($"old-type-{i}")}',$,'Member',$,$,$,$,'500',$,.NOTDEFINED.");
            writer.Add("IFCRELDEFINESBYTYPE",
                $"'{IfcGuid.FromName($"old-rel-{i}")}',900,$,$,(#{1001 + i}),#{type}");
            sources[type] = guid;
        }
        Assert.Equal((0, 0), Apply(writer, sources));
    }

    [Fact]
    public void SeveralGInstances_AreKeyedByTheOneThatIsAFamilySymbol()
    {
        var elements = Proxies(
            [601, 701], [602, 701], [603, 702]);
        var (text, sources) = Write(elements, "IFCBUILDINGELEMENTPROXY", currentProfile: true);
        Assert.Equal(2, Regex.Matches(text, "=IFCBUILDINGELEMENTPROXYTYPE\\(").Count);
        Assert.Contains("(#1001,#1002),#", text);
        Assert.Contains("(#1003),#", text);
        Assert.Equal(
            new[] { IfcGuid.ToIfcGuid(FirstSymbolExportId), IfcGuid.ToIfcGuid(SecondSymbolExportId) }.Order(),
            sources.Values.Order());
    }

    [Fact]
    public void SeveralGInstances_WithoutAnyFamilySymbol_FallBackToTheLastOne()
    {
        var elements = Proxies([601, 602], [603, 602], [602, 603]);
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY", currentProfile: true).Text;
        Assert.Equal(2, Regex.Matches(text, "=IFCBUILDINGELEMENTPROXYTYPE\\(").Count);
        Assert.Contains("(#1001,#1002),#", text);
        Assert.Contains("(#1003),#", text);
    }

    [Fact]
    public void SeveralGInstances_KeepTheProductUntypedInTheOlderRelease()
    {
        var elements = Proxies([601, 701], [602, 701]);
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY", currentProfile: false).Text;
        Assert.DoesNotContain("TYPE(", text);
        Assert.DoesNotContain("=IFCRELDEFINESBYTYPE(", text);
    }

    [Fact]
    public void SeveralGInstances_WithAnUnknownSymbol_KeepTheProductUntyped()
    {
        var elements = Proxies([601, 999], [602, 701]);
        string text = Write(elements, "IFCBUILDINGELEMENTPROXY", currentProfile: true).Text;
        Assert.Single(Regex.Matches(text, "=IFCBUILDINGELEMENTPROXYTYPE\\("));
        Assert.Contains("(#1002),#", text);
        Assert.DoesNotContain("#1001", text);
    }

    [Fact]
    public void StairRun_IsTypedByItsTypeIdWhenItCarriesNoTypeParameter()
    {
        var elements = StairParts(-2000919, null);
        var (text, sources) = Write(elements, "IFCSTAIRFLIGHT", currentProfile: true);
        Assert.Single(Regex.Matches(text, "=IFCSTAIRFLIGHTTYPE\\("));
        Assert.Contains("(#1001),#", text);
        // The GlobalId of the run type is the ExportId of the type element.
        Assert.Equal(IfcGuid.ToIfcGuid(TypeExportId), Assert.Single(sources).Value);
        Assert.DoesNotContain("TYPE(", Write(elements, "IFCSTAIRFLIGHT", currentProfile: false).Text);
    }

    [Fact]
    public void StairLanding_IsTypedByItsTypeIdNotBySiblingLandingNamedByTheParameter()
    {
        var elements = StairParts(-2000920, 725395);
        string text = Write(elements, "IFCSLAB", currentProfile: true).Text;
        Assert.Single(Regex.Matches(text, "=IFCSLABTYPE\\("));
        Assert.Contains("'171672'", text);   // the Tag of the type is the id of the type element
        Assert.DoesNotContain("'725395'", text);
    }

    [Fact]
    public void Stair_IsTypedByAnIfcTypeProductOfEightAttributes()
    {
        var elements = StairParts(-2000120, null);
        var (text, sources) = Write(elements, "IFCSTAIR", currentProfile: true);
        var type = Assert.Single(text.Split('\n'), line => Regex.IsMatch(line, @"^#\d+=IFCTYPEPRODUCT\("));
        string[] args = Args(type);
        Assert.Equal(8, args.Length);
        Assert.Equal("'Stair type'", args[2]);
        Assert.Equal("$", args[5]);
        Assert.Equal("'171672'", args[7]);
        Assert.Contains("(#1001),#", text);
        Assert.Equal(IfcGuid.ToIfcGuid(TypeExportId), Assert.Single(sources).Value);
        Assert.DoesNotContain("IFCTYPEPRODUCT(", Write(elements, "IFCSTAIR", currentProfile: false).Text);
    }

    [Theory]
    [InlineData(-2000919, 171672, 0, 171672)]   // a run: TypeId is the type
    [InlineData(-2000919, 171672, 500, 171672)] // the parameter does not matter for the parts of a stair
    [InlineData(-2000020, 171672, 500, 500)]    // not a part of a stair
    public void TypeObjectElement_OfAStairPartIsTheElementWithoutATypeOfItsOwn(
        int category, int typeId, int parameterValue, int expected)
    {
        var elements = StairParts(category, parameterValue == 0 ? null : parameterValue);
        var instance = elements[1];
        Assert.Equal(expected, IfcExporter.ResolveTypeObjectElementId(instance, elements, currentProfile: true));
        Assert.Equal(parameterValue == 0 ? -1 : parameterValue, IfcExporter.ResolveTypeObjectElementId(instance, elements, currentProfile: false));
        // A TypeId that is itself typed (an instance) is no type.
        elements[typeId].TypeId = 99;
        Assert.Equal(parameterValue == 0 ? -1 : parameterValue, IfcExporter.ResolveTypeObjectElementId(instance, elements, currentProfile: true));
    }

    [Theory]
    [InlineData("IFCDOOR", -2000023, "IFCDOORSTYLE", true)]
    [InlineData("IFCWINDOW", -2000014, "IFCWINDOWSTYLE", true)]
    [InlineData("IFCDOOR", -2000023, "IFCDOORSTYLE", false)]
    public void DoorAndWindowStyle_TakeTheDoorStyleSubElementOfTheSymbolInTheNewerRelease(
        string product, int category, string typeClass, bool currentProfile)
    {
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Style", CategoryId = category },
            [601] = new() { Id = 601, Name = "Symbol", CategoryId = category, SerializedExportId = TypeExportId },
            [1] = new Element
            {
                Id = 1, CategoryId = category, ValidatedOriginalFamilySymbolId = 601,
                Parameters = [new Parameter
                {
                    Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                    StorageType = StorageType.ElementId, Value = 500,
                }],
            },
        };
        var (text, sources) = Write(elements, product, currentProfile);
        Assert.Single(Regex.Matches(text, $"={typeClass}\\("));
        Guid expected = currentProfile ? ElementExportIds.SubElementId(TypeExportId, IfcExporter.DoorStyleSubElementIndex) : TypeExportId;
        Assert.Equal(IfcGuid.ToIfcGuid(expected), Assert.Single(sources).Value);
        Assert.NotEqual(IfcGuid.ToIfcGuid(TypeExportId), IfcGuid.ToIfcGuid(ElementExportIds.SubElementId(TypeExportId, 21)));
    }

    [Fact]
    public void DuctSegmentTypes_TakeTheExportIdOfTheTypeElementInTheNewerReleaseOnly()
    {
        var elements = Segments(-2008000, -1140333, 700, 701);
        var newer = Write(elements, "IFCFLOWSEGMENT", currentProfile: true).Sources;
        Assert.Equal(2, newer.Count);
        Assert.All(newer.Values, guid => Assert.Equal(IfcGuid.ToIfcGuid(TypeExportId), guid));
        Assert.Empty(Write(elements, "IFCFLOWSEGMENT", currentProfile: false).Sources);
    }

    [Fact]
    public void ConnectedSystemTypes_GoToElementsWithoutOneOfTheirOwn()
    {
        var derived = IfcExporter.ResolveConnectedSystemTypes(Network());
        Assert.Equal(700, derived[1]);                // fitting without connector data, next to a pipe of system type 700
        Assert.Equal(700, derived[5]);                // next to that fitting only
        Assert.False(derived.ContainsKey(4));         // pipes of two system types meet at it
        Assert.False(derived.ContainsKey(11));        // a pipe has its own
        Assert.False(derived.ContainsKey(13));        // several system types of its own
        Assert.False(derived.ContainsKey(7));         // only a discovered record connects it
    }

    [Fact]
    public void FittingWithoutASystemType_TakesTheOneOfItsNeighbours_InTheNewerRelease()
    {
        var elements = Network();
        string newer = Write(elements, "IFCFLOWFITTING", currentProfile: true).Text;
        // Fitting 1 has no system type, the pipe next to it is of type 700 like the parameter of fitting 2; fitting 3 has 701.
        Assert.Equal(2, Regex.Matches(newer, "=IFCPIPEFITTINGTYPE\\(").Count);
        Assert.Contains("(#1001,#1002),#", newer);
        Assert.Contains("(#1003),#", newer);
        string older = Write(elements, "IFCFLOWFITTING", currentProfile: false).Text;
        Assert.Equal(3, Regex.Matches(older, "=IFCPIPEFITTINGTYPE\\(").Count);
    }

    /// <summary>Fittings 1 (no system type parameter), 2 (parameter 700) and 3 (701) of one symbol. Pipe 11 (system type
    /// 700) is next to fittings 1 and 4, pipe 12 (701) next to 4, element 5 (no connector data) is next to fitting 1, pipe 13 is
    /// in two systems, element 7 is connected by a discovered record only.</summary>
    private static Dictionary<int, Element> Network()
    {
        Element System(int id, int type) => new() { Id = id, SerializedMepSystem = ("system", type) };
        Element Pipe(int id, int[] systems, params int[][] refs) => new()
        {
            Id = id,
            SerializedConnectorGraph = refs.Select((r, k) => (k, 0,
                (IReadOnlyList<(int Id, int Index, int Type)>)r.Select(target => (target, 0, 0)).ToList())).ToList(),
            SerializedConnectorSystems = systems,
        };
        Element Fitting(int id, int? systemType) => new()
        {
            Id = id, CategoryId = -2008049,
            Parameters = new[] { new Parameter { Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" }, StorageType = StorageType.ElementId, Value = 500 } }
                .Concat(systemType is { } type ? [new Parameter { Id = -1140334, StorageType = StorageType.ElementId, Value = type }] : []).ToList(),
            Geometry = new GeometryData { ExactMappedInstances = [new GeoInstance { SymbolId = 601 }] },
        };
        var discovered = Pipe(8, [900], [7]);
        discovered.IsDiscovered = true;
        return new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Tee", CategoryId = -2008049 },
            [601] = new() { Id = 601, Name = "Symbol" },
            [900] = System(900, 700), [901] = System(901, 701),
            [1] = Fitting(1, null), [2] = Fitting(2, 700), [3] = Fitting(3, 701),
            [4] = new Element { Id = 4 }, [5] = Pipe(5, [0], [1]), [7] = new Element { Id = 7 }, [8] = discovered,
            [11] = Pipe(11, [900, 900], [1], [4]), [12] = Pipe(12, [901], [4]), [13] = Pipe(13, [900, 901], [4], [4]),
        };
    }

    /// <summary>The type 171672 (no type of its own) and the instance 1 that names it by TypeId only; a nonnull
    /// <paramref name="elemTypeParam"/> adds an ELEM_TYPE_PARAM that points at a sibling instance, as a landing does.</summary>
    private static Dictionary<int, Element> StairParts(int category, int? elemTypeParam)
    {
        var elements = new Dictionary<int, Element>
        {
            [171672] = new() { Id = 171672, CategoryId = category, Name = "Stair type", SerializedExportId = TypeExportId },
            [1] = new() { Id = 1, CategoryId = category, TypeId = 171672 },
            [500] = new() { Id = 500, CategoryId = category, TypeId = 499 },
            [499] = new() { Id = 499, CategoryId = category },
        };
        if (elemTypeParam is { } target)
            elements[1].Parameters.Add(new Parameter
            {
                Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                StorageType = StorageType.ElementId, Value = target,
            });
        if (elemTypeParam == 725395)
            elements[725395] = new Element { Id = 725395, CategoryId = category, TypeId = 725394 };
        return elements;
    }

    // Arguments of a whole entity line ("#12=IFCX(a,b);").
    private static string[] Args(string line)
    {
        line = line.TrimEnd('\r');
        return line[(line.IndexOf('(') + 1)..^2].Split(',');
    }

    private static Dictionary<int, Element> Segments(int category, int parameterId, params int[] systemTypes)
    {
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Pipe type", CategoryId = category, SerializedExportId = TypeExportId },
        };
        for (int i = 0; i < systemTypes.Length; i++)
            elements[i + 1] = new Element
            {
                Id = i + 1, CategoryId = category,
                Parameters =
                [
                    new Parameter
                    {
                        Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                        StorageType = StorageType.ElementId, Value = 500,
                    },
                    new Parameter { Id = parameterId, StorageType = StorageType.ElementId, Value = systemTypes[i] },
                ],
            };
        return elements;
    }

    /// <summary>One generic model type; instance i has a geometry of the GInstance symbols listed for it. Symbols 601 to
    /// 603 are not family symbols (no ExportId), 701 and 702 are.</summary>
    private static Dictionary<int, Element> Proxies(params int[][] symbolsPerInstance)
    {
        var elements = new Dictionary<int, Element>
        {
            [500] = new() { Id = 500, Name = "Shared type", CategoryId = -2000151 },
            [601] = new() { Id = 601, Name = "Nested 1" }, [602] = new() { Id = 602, Name = "Nested 2" },
            [603] = new() { Id = 603, Name = "Nested 3" },
            [701] = new() { Id = 701, Name = "Symbol 1", SerializedExportId = FirstSymbolExportId },
            [702] = new() { Id = 702, Name = "Symbol 2", SerializedExportId = SecondSymbolExportId },
        };
        for (int i = 0; i < symbolsPerInstance.Length; i++)
            elements[i + 1] = new Element
            {
                Id = i + 1, CategoryId = -2000151,
                Parameters =
                [
                    new Parameter
                    {
                        Id = -1002050, Definition = new Definition { Name = "ELEM_TYPE_PARAM" },
                        StorageType = StorageType.ElementId, Value = 500,
                    },
                ],
                Geometry = new GeometryData
                {
                    ExactMappedInstances = symbolsPerInstance[i].Select(symbol => new GeoInstance { SymbolId = symbol }).ToList(),
                },
            };
        return elements;
    }

    private static (string Text, Dictionary<int, string> Sources) Write(
        Dictionary<int, Element> elements, string product, bool currentProfile)
    {
        var products = new Dictionary<int, (int entityId, int placementId, string ifcType)>();
        foreach (int id in new[] { 1, 2, 3 })
            if (elements.ContainsKey(id)) products.Add(id, (1000 + id, 0, product));
        var writer = new StepWriter();
        var method = typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByTypeCore",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var sources = Assert.IsType<Dictionary<int, string>>(method.Invoke(null, [writer, 900, elements, products,
            new Dictionary<(string PropertyName, string ValueExpr), int>(), new Dictionary<int, bool?>(), currentProfile]));
        using var text = new StringWriter();
        writer.WriteTo(text, "test.ifc", "IFC2X3", "test", "test");
        return (text.ToString(), sources);
    }

    private static (int Types, int Relationships) Apply(StepWriter writer, Dictionary<int, string> sources)
    {
        var method = typeof(IfcExporter).GetMethod("ApplyTypeGlobalIds", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<(int, int)>(method.Invoke(null, [writer, sources]));
    }
}