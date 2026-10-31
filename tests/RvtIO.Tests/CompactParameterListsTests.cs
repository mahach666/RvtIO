using RvtIO.Compact;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The built-in parameter id lists per element class. The expected lists of the vector tests are the lists the current profile reports
/// for the named elements of the validation projects (element id in the test name); the other tests state one rule each.
/// </summary>
public class CompactParameterListsTests
{
    private static readonly CompactListFileFacts Plain = new(false, true, false);
    private static readonly CompactListFileFacts WithDesignOptions = new(false, true, true);

    private static int ClassTag(string name) =>
        TfBlockDeserializer.Schemas.Single(pair => pair.Value.Name == name).Key;

    private static Element Make(int family = -1, int ownerView = -1, int[]? cellClasses = null, int[]? stored = null, bool storesComplete = true)
    {
        var element = new Element
        {
            Id = 1,
            SerializedFamilyId = family,
            SerializedUnplacedOwnerId = -1,
            SerializedOwnerDbViewId = ownerView,
            SerializedCreatedPhaseId = -1,
            NativeCellListTag = cellClasses is null ? 0 : 653,
            NativeCellClasses = cellClasses,
            NativeParameterStoreIds = [.. stored ?? []],
            NativeParameterStoresComplete = storesComplete,
        };
        return element;
    }

    private static CompactListResult Run(string recordClass, Element element, int header = -1, CompactListFileFacts? file = null,
        Func<int, Element?>? resolve = null) =>
        CompactParameterLists.GetBuiltinIds(recordClass, new ElementListFacts(element, header, file ?? Plain, resolve));

    /// <summary>A dimension (flags, style id) without sketch plane and label, and the dimension style it refers to.</summary>
    private static (Element Dimension, Func<int, Element?> Resolve) Dimension(int flags, int styleId, int[] cellClasses,
        int styleType = 0, int linearType = 1)
    {
        var dimension = Make(cellClasses: cellClasses);
        dimension.NativeListFields = new()
        {
            ["160:m_dimSketchPlaneId"] = -1, ["160:m_flags"] = flags, ["160:m_showLabelGP"] = 0, ["160:m_styleSymbolId"] = styleId,
        };
        var style = new Element
        {
            Id = styleId,
            NativeListFields = new() { ["174:m_dimensionStyleType"] = styleType, ["174:m_linearDimType"] = linearType },
        };
        return (dimension, id => id == styleId ? style : null);
    }

    [Fact]
    public void GStyleElem_Test2022_30_ListsTheBaseIds()
    {
        var result = Run("GStyleElem", Make(cellClasses: [ClassTag("PatternHelper")]));

        Assert.Null(result.Unknown);
        Assert.Equal([-1140363, -1140362, -1013201, -1002002, -1002001], result.Ids);
    }

    [Fact]
    public void GStyleElem_StructuralBasicSample_30_DesignOptionSetsAndNoOwnerViewAddView()
    {
        var result = Run("GStyleElem", Make(), file: WithDesignOptions);

        Assert.Equal([-1140363, -1140362, -1013201, -1013200, -1002002, -1002001], result.Ids);
    }

    [Fact]
    public void DesignOptionSets_ButAnOwnerView_DoNotAddTheDesignOptionId()
    {
        var result = Run("GStyleElem", Make(ownerView: 272132), file: WithDesignOptions);

        Assert.DoesNotContain(-1013200, result.Ids!);
        Assert.Equal(5, result.Ids!.Length);
    }

    [Fact]
    public void Workshared_AddsTheWorksharingIds()
    {
        var shared = Run("GStyleElem", Make(), file: new CompactListFileFacts(true, true, false));
        var local = Run("GStyleElem", Make());

        Assert.Equal(local.Ids!.Concat([-1002067, -1002053]).Order(), shared.Ids);
    }

    [Fact]
    public void LeaderStyle_Test2022_234_AddsClassIdsToTheStoredOnes()
    {
        var element = Make(cellClasses: [ClassTag("PatternHelper")], stored: [-1006447, -1006426, -1006414]);

        var result = Run("LeaderStyle", element);

        Assert.Equal([-1140363, -1140362, -1013201, -1006524, -1006449, -1006447, -1006426, -1006425, -1006414, -1006413, -1002002, -1002001],
            result.Ids);
    }

    [Fact]
    public void RefTableElem_ArchLink_272153_IgnoresStoredUserParameterIds()
    {
        var result = Run("RefTableElem", Make(ownerView: 272132, stored: [272138, 272140, 272142, 272144, 272150]));

        Assert.Equal([-1140363, -1140362, -1013201, -1007850, -1002002, -1002001], result.Ids);
    }

    [Fact]
    public void AreaReinforcementType_StructuralAdvancedSample_184658_CategoryWithPhasesListsTheTypeMark()
    {
        var result = Run("AreaReinforcementType", Make(), header: -2009003);

        Assert.Equal(
            [-1152384, -1140422, -1140363, -1140362, -1019017, -1019015, -1019013, -1019001, -1013201, -1010109, -1010108, -1010105,
             -1010104, -1010103, -1002501, -1002500, -1002002, -1002001, -1001405, -1001205],
            result.Ids);
    }

    [Fact]
    public void Hub_Test2022_414562_CellListAddsItsParameter()
    {
        var element = Make(cellClasses: [ClassTag("HubByPointCell"), ClassTag("NodeCell")]);

        var result = Run("Hub", element, header: -2009645);

        Assert.Equal([-1140363, -1140362, -1013201, -1010106, -1002002, -1002001], result.Ids);
    }

    [Fact]
    public void RefPlane_Test2022_243275_IsNotInAFamily()
    {
        var result = Run("RefPlane", Make(cellClasses: [3606], stored: [-1012201]), header: -2000530);

        Assert.Equal([-1140363, -1140362, -1013201, -1012201, -1008000, -1006220, -1002002, -1002001], result.Ids);
    }

    [Fact]
    public void RefPlane_ArchLink_246764_InAFamilyAddsTheFamilyParameters()
    {
        var result = Run("RefPlane", Make(family: 246698, cellClasses: [3606], stored: [-1012201]), header: -2000530);

        Assert.Equal([-1140363, -1140362, -1013201, -1012201, -1008000, -1006220, -1004012, -1004003, -1004002, -1002002, -1002001],
            result.Ids);
    }

    [Fact]
    public void LinearDimString_Test2022_549060_StyleFieldsDecideTheTextParameters()
    {
        var (dimension, resolve) = Dimension(flags: 28, styleId: 277, cellClasses: [3606]);

        var result = Run("LinearDimString", dimension, header: -2000261, resolve: resolve);

        Assert.Equal([-1140363, -1140362, -1013201, -1004515, -1004510, -1004500, -1002052, -1002051, -1002050, -1002002, -1002001, -1002000],
            result.Ids);
    }

    [Fact]
    public void LinearDimString_Test2022_800283_FlagBit64DropsTheTextParameters()
    {
        var (dimension, resolve) = Dimension(flags: 76, styleId: 182551, cellClasses: [474, 1332]);

        var result = Run("LinearDimString", dimension, header: -2000262, resolve: resolve);

        Assert.Equal([-1140363, -1140362, -1013201, -1004515, -1002052, -1002051, -1002050, -1002002, -1002001, -1002000], result.Ids);
    }

    [Fact]
    public void LinearDimString_WithoutTheStyleElement_IsUnknown()
    {
        var (dimension, _) = Dimension(flags: 28, styleId: 277, cellClasses: [3606]);

        var result = Run("LinearDimString", dimension, header: -2000261);

        Assert.Null(result.Ids);
        Assert.StartsWith("fact dimStyle", result.Unknown);
    }

    [Fact]
    public void Class_IsTheRecordClassName()
    {
        var element = Make();
        Assert.Null(ElementListFacts.RecordClassName(element));

        element.NativeRecordClass = ClassTag("GStyleElem");
        Assert.Equal("GStyleElem", ElementListFacts.RecordClassName(element));
    }

    [Fact]
    public void RepeatingDetailAttributes_Sample_72802_NeverListsItsStoredParameters()
    {
        // the class lists the five default ids only: a stored id is not added, and stores that were not decoded do not matter
        var element = Make(stored: [-1005501]);
        Assert.Equal([-1140363, -1140362, -1013201, -1002002, -1002001], Run("RepeatingDetailAttributes", element).Ids);

        var undecoded = Make(storesComplete: false);
        Assert.Equal([-1140363, -1140362, -1013201, -1002002, -1002001], Run("RepeatingDetailAttributes", undecoded).Ids);
    }

    [Fact]
    public void ClassWithoutRules_IsUnknown()
    {
        var result = Run("StairsRun", Make());

        Assert.False(CompactParameterLists.IsSupported("StairsRun"));
        Assert.Null(result.Ids);
        Assert.Contains("not covered", result.Unknown);
    }

    [Fact]
    public void UnknownDocumentFacts_AreNamedInsteadOfGuessed()
    {
        var result = Run("GStyleElem", Make(), file: CompactListFileFacts.Unknown);

        Assert.Null(result.Ids);
        Assert.StartsWith("fact doc", result.Unknown);
    }

    [Fact]
    public void UndecodedCellList_IsUnknown()
    {
        var element = Make();
        element.NativeCellListTag = null;

        var result = Run("GStyleElem", element);

        Assert.Null(result.Ids);
        Assert.StartsWith("fact cellHas:", result.Unknown);
    }

    [Fact]
    public void CellWhoseContentDecidesItsParameters_IsUnknown()
    {
        var result = Run("GStyleElem", Make(cellClasses: [ClassTag("PointOnFaceData")]));

        Assert.Null(result.Ids);
        Assert.NotNull(result.Unknown);
    }

    [Fact]
    public void IncompleteParameterStores_AreUnknown()
    {
        var result = Run("GStyleElem", Make(storesComplete: false));

        Assert.Null(result.Ids);
        Assert.Equal("stored parameter ids unknown", result.Unknown);
    }

    [Fact]
    public void StoredIdWithoutVerdict_IsUnknown()
    {
        var result = Run("GStyleElem", Make(stored: [-1999999]));

        Assert.Null(result.Ids);
        Assert.Equal("stored id -1999999 not validated", result.Unknown);
    }

    [Fact]
    public void StoredTypeMark_IsNotListedByTheStoredPass()
    {
        var result = Run("GStyleElem", Make(stored: [-1001405]));

        Assert.Equal([-1140363, -1140362, -1013201, -1002002, -1002001], result.Ids);
    }

    [Fact]
    public void StoredVerdicts_AndPhaseCategories_AreOnlyAnsweredWhenValidated()
    {
        Assert.True(CompactParameterLists.StoredIdIsListed(-1006447));
        Assert.Null(CompactParameterLists.StoredIdIsListed(-1999999));
        Assert.True(CompactParameterLists.CategoryHasPhases(-2009003));
        Assert.Null(CompactParameterLists.CategoryHasPhases(-2000127));
    }

    [Theory]
    [InlineData(-2008049)]      // categories met first in the files of Revit 2023 to 2026
    [InlineData(-2008010)]
    [InlineData(-2001060)]
    [InlineData(-2000011)]
    public void ObservedPhaseCategories_HavePhases(int category)
        => Assert.True(CompactParameterLists.CategoryHasPhases(category));

    [Fact]
    public void Tables_AreConsistent()
    {
        foreach (var (name, groups, _, exact, total) in CompactParameterListRules.Classes)
        {
            Assert.InRange(exact, 1, total);
            Assert.All(groups, g => Assert.InRange(g, 0, CompactParameterListRules.Groups.Length - 1));
            Assert.Equal((exact, total), CompactParameterLists.Source(name));
        }
        // every scalar field a rule reads is one the reader captures
        Assert.All(CompactParameterListRules.Facts.Where(f => f.StartsWith("field:", StringComparison.Ordinal)),
            f => Assert.Contains(f["field:".Length..], TfBlockDeserializer.ListFactFieldKeys));
        Assert.All(CompactParameterListRules.Groups, g =>
        {
            int root = int.Parse(g[(g.IndexOf('|') + 1)..]);
            Assert.InRange(root, 0, CompactParameterListRules.Nodes.Length - 1);
        });
    }

    [Fact]
    public void Ids_AreAscendingAndFreeOfDuplicates()
    {
        var result = Run("AreaReinforcementType", Make(), header: -2009003, file: new CompactListFileFacts(true, true, true));

        Assert.Equal(result.Ids!.Distinct().Order(), result.Ids);
    }
}
