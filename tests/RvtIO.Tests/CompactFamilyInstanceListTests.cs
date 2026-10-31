using RvtIO.Compact;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The parameter id list of a FamilyInstance. The vector tests rebuild the inputs of four family instances of Test_2022 (instance,
/// symbol and family records as the reader decodes them) and expect the list the current profile reports for the element; the other tests
/// state one rule each.
/// </summary>
public class CompactFamilyInstanceListTests
{
    private static readonly CompactListFileFacts Plain = new(false, true, false);

    private static int Tag(string name) => TfBlockDeserializer.Schemas.Single(pair => pair.Value.Name == name).Key;

    private static Dictionary<string, int> Fields(params (string Key, int Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);

    private static Element Record(int id, string tfClass, Dictionary<string, int> fields, int[] cells, (int, bool)[]? named = null,
        bool tailComplete = true, int createdPhase = -1) => new()
    {
        Id = id,
        NativeRecordClass = Tag(tfClass),
        SerializedFamilyId = -1,
        SerializedUnplacedOwnerId = -1,
        SerializedOwnerDbViewId = -1,
        SerializedCreatedPhaseId = createdPhase,
        NativeCellListTag = 653,
        NativeCellClasses = cells,
        NativeParameterStoreIds = [],
        NativeParameterStoresComplete = true,
        NativeListFields = fields,
        NativeTailWalkComplete = tailComplete,
        NativeNamedParameters = named,
    };

    private static readonly (string, int)[] NoDriver =
    [
        ("1794:m_assocSketchPlaneId", -1), ("1794:m_oFamInstSpec", 0), ("1794:m_pCurveDriver", 0), ("1794:m_pDesignPropManager", 0),
        ("1794:m_pInstParams", 714), ("1794:m_scheduleOnlyLevelId", -1), ("1794:m_structType", 0), ("1794:m_workPlaneBased", 0), ("37:m_dummy", 0),
    ];

    private static Dictionary<string, int> InstanceFields(int symbol, int userData, int refs, int refFirst, int useOffset, int assocLevel, params (string, int)[] more) =>
        Fields([.. NoDriver, ("1794:m_masterSymbolId", symbol), ("1794:m_oFamInstUserData", userData), ("1794:m_refs#", refs),
                ("1794:m_refs@0", refFirst), ("1794:m_useOffsetPos", useOffset), ("37:m_assocLevelId", assocLevel), .. more]);

    private static Dictionary<string, int> FamilyFields(int category, int structMaterial, int hostType, int curtain, int dummyRefs) =>
        Fields(("1772:m_bHasAnyDummyRefs", dummyRefs), ("1772:m_elevationFixed", 0), ("1772:m_refTypeIds@0", hostType),
               ("1773:m_categoryId", category), ("1773:m_eStructMaterialType", structMaterial), ("1773:m_eSymbolicRepType", 0),
               ("1773:m_inPlace", 0), ("1773:m_is2d", 0), ("1773:m_isCrvDriven", 0), ("1773:m_isCurtainPanel", curtain),
               ("1773:m_isModel", 1), ("1773:m_isWorkPlaneBased", 0), ("1773:m_lightSourceDefBits", 0));

    private static (Element Instance, Func<int, Element?> Resolve) Chain(Element instance, Element symbol, Element family)
    {
        var all = new[] { instance, symbol, family }.ToDictionary(e => e.Id);
        return (instance, id => all.GetValueOrDefault(id));
    }

    private static CompactListResult Run(Element instance, Func<int, Element?> resolve, int header) =>
        CompactParameterLists.GetBuiltinIds("FamilyInstance", new ElementListFacts(instance, header, Plain, resolve));

    [Fact]
    public void CurtainPanelInstance_Test2022_423100()
    {
        var symbol = Record(232758, "SysPanelFamSym", Fields(("1829:m_familyId", 12609), ("1829:m_pParams", 714)), [3913]);
        var family = Record(12609, "Family", FamilyFields(-2000170, 4, 0, 1, 1), [1809, 496], tailComplete: false);
        var instance = Record(423100, "FamilyInstance", InstanceFields(232758, 0, 1, 897, 0, 311), [311, 1800], createdPhase: 86961);
        var (element, resolve) = Chain(instance, symbol, family);

        var result = Run(element, resolve, -2000170);

        Assert.Null(result.Unknown);
        Assert.Equal([-1152385, -1140363, -1140362, -1019016, -1019014, -1019012, -1019000, -1013201, -1012806, -1012805, -1012101, -1012100,
            -1010303, -1010301, -1010300, -1010106, -1002108, -1002062, -1002052, -1002051, -1002050, -1002002, -1002001, -1002000,
            -1001301, -1001300, -1001203], result.Ids);
    }

    [Fact]
    public void StructuralColumnWithUserDataAndCoverSettings_Test2022_414482()
    {
        var symbol = Record(420952, "FamilySymbol", Fields(("1829:m_familyId", 412500), ("1829:m_pParams", 714)), [1831], [(-1005500, true), (-1002500, false)]);
        var family = Record(412500, "Family", FamilyFields(-2001330, 2, 4, 0, 1), [1809], tailComplete: false);
        var instance = Record(414482, "FamilyInstance",
            InstanceFields(420952, Tag("StructColumnUserData"), 2, 1801, 1, 311, ("877:m_active", 1)),
            [308, 311, Tag("CoverSettingsCell"), 3799, 1800], [(-1005500, false)], createdPhase: 86961);
        var (element, resolve) = Chain(instance, symbol, family);

        var result = Run(element, resolve, -2001330);

        Assert.Null(result.Unknown);
        Assert.Equal([-1152385, -1150171, -1140363, -1140362, -1019016, -1019014, -1019012, -1019000, -1013439, -1013438, -1013437, -1013201,
            -1012806, -1012101, -1012100, -1010106, -1005500, -1002563, -1002108, -1002066, -1002065, -1002064, -1002063, -1002062, -1002052,
            -1002051, -1002050, -1002002, -1002001, -1002000, -1001552, -1001375, -1001371, -1001358, -1001357, -1001351, -1001350, -1001203,
            -1001007], result.Ids);
    }

    [Fact]
    public void StructuralFootingUserDataAddsItsSixIds_Test2022_512277()
    {
        var symbol = Record(512173, "FamilySymbol", Fields(("1829:m_familyId", 563585), ("1829:m_pParams", 714)), [1831],
            [(-1001568, false), (-1005500, true), (-1001569, false), (-1002500, false)]);
        var family = Record(563585, "Family", FamilyFields(-2001300, 2, 0, 0, 0), [1809], tailComplete: false);
        var instance = Record(512277, "FamilyInstance",
            InstanceFields(512173, Tag("StructFootingUserData"), 0, 0, 1, 511122, ("877:m_active", 1)),
            [308, 311, Tag("CoverSettingsCell"), 1800], [(-1005500, false)], createdPhase: 86961);
        var (element, resolve) = Chain(instance, symbol, family);

        var result = Run(element, resolve, -2001300);

        Assert.Null(result.Unknown);
        Assert.Equal([-1152385, -1140363, -1140362, -1019016, -1019014, -1019012, -1019000, -1013439, -1013438, -1013437, -1013201, -1012806,
            -1012805, -1012101, -1012100, -1010106, -1005500, -1002108, -1002062, -1002052, -1002051, -1002050, -1002002, -1002001, -1002000,
            -1001951, -1001658, -1001657, -1001598, -1001567, -1001561, -1001558, -1001552, -1001375, -1001371, -1001363, -1001352,
            -1001203], result.Ids);
    }

    [Fact]
    public void ModelGroupInstanceMergesTheItemsOfItsOwnAndItsSymbolsParameters_Test2022_566599()
    {
        var symbol = Record(12344, "FamilySymbol", Fields(("1829:m_familyId", 157312), ("1829:m_pParams", 714)), [1831],
            [(-1008623, true), (-1008625, true), (-1008633, false), (-1008624, true), (-1008622, true)]);
        var family = Record(157312, "Family", FamilyFields(-2000127, 4, 0, 0, 0), [1809, 496], tailComplete: false);
        var instance = Record(566599, "FamilyInstance", InstanceFields(12344, 0, 0, 0, 0, -1), [311, 1800],
            [(-1008623, false), (-1008625, false), (-1008624, false), (-1008622, false)]);
        var (element, resolve) = Chain(instance, symbol, family);

        var result = Run(element, resolve, -2000127);

        Assert.Null(result.Unknown);
        Assert.Equal([-1152385, -1140363, -1140362, -1019016, -1019014, -1019012, -1019000, -1013201, -1012806, -1012805, -1010106, -1008625,
            -1008624, -1008623, -1008622, -1002108, -1002062, -1002052, -1002051, -1002050, -1002002, -1002001, -1002000, -1001370, -1001360,
            -1001203], result.Ids);
    }

    [Fact]
    public void StructuralFramingUserData_IsNotModelled()
    {
        var symbol = Record(420952, "FamilySymbol", Fields(("1829:m_familyId", 412500), ("1829:m_pParams", 714)), [1831]);
        var family = Record(412500, "Family", FamilyFields(-2001320, 2, 4, 0, 1), [1809]);
        var instance = Record(1, "FamilyInstance", InstanceFields(420952, Tag("StructFramUserData"), 0, 0, 1, 311), [311, 1800]);
        var (element, resolve) = Chain(instance, symbol, family);

        Assert.NotNull(Run(element, resolve, -2001320).Unknown);
    }

    [Fact]
    public void WithoutTheSymbolRecord_TheListIsUnknown()
    {
        var instance = Record(1, "FamilyInstance", InstanceFields(420952, 0, 0, 0, 0, 311), [311, 1800]);

        Assert.NotNull(Run(instance, _ => null, -2000170).Unknown);
    }

    [Fact]
    public void AnInPlaceModelFamily_DependsOnTheMoveRestrictions_AndIsUnknown()
    {
        var symbol = Record(2, "FamilySymbol", Fields(("1829:m_familyId", 3), ("1829:m_pParams", 714)), [1831]);
        var fields = FamilyFields(-2000170, 4, 0, 0, 0);
        fields["1773:m_inPlace"] = 1;
        var family = Record(3, "Family", fields, [1809]);
        var instance = Record(1, "FamilyInstance", InstanceFields(2, 0, 0, 0, 0, 311), [311, 1800]);
        var (element, resolve) = Chain(instance, symbol, family);

        Assert.Contains("canMoveElevation", Run(element, resolve, -2000170).Unknown);
    }


    private static Dictionary<string, int> SymbolFamilyFields(int category, int isModel, int partType) =>
        Fields(("1772:m_bHasAnyDummyRefs", 0), ("1772:m_elevationFixed", 0), ("1772:m_refTypeIds@0", 0), ("1773:m_categoryId", category),
               ("1773:m_eStructMaterialType", 4), ("1773:m_eSymbolicRepType", 0), ("1773:m_inPlace", 0), ("1773:m_is2d", 0),
               ("1773:m_isCrvDriven", 0), ("1773:m_isCurtainPanel", 0), ("1773:m_isModel", isModel), ("1773:m_isWorkPlaneBased", 0),
               ("1773:m_lightSourceDefBits", 0), ("1773:m_partType", partType));

    [Fact]
    public void ModelGroupSymbol_Test2022_12344()
    {
        var symbol = Record(12344, "FamilySymbol", Fields(("1829:m_familyId", 157312), ("1829:m_pParams", 714)), [1831],
            [(-1008623, true), (-1008625, true), (-1008633, false), (-1008624, true), (-1008622, true)]);
        var family = Record(157312, "Family", SymbolFamilyFields(-2000127, 1, -1), [1809, 496], tailComplete: false);
        var all = new[] { symbol, family }.ToDictionary(e => e.Id);

        var result = CompactParameterLists.GetBuiltinIds("FamilySymbol", new ElementListFacts(symbol, -2000127, Plain, id => all.GetValueOrDefault(id)));

        Assert.Null(result.Unknown);
        Assert.Equal([-1152384, -1140422, -1140363, -1140362, -1019017, -1019015, -1019013, -1019001, -1013201, -1010109, -1010108, -1010105,
            -1010104, -1010103, -1008633, -1005556, -1002503, -1002502, -1002501, -1002500, -1002002, -1002001, -1001405, -1001205], result.Ids);
    }

    [Fact]
    public void CurtainPanelSymbol_Test2022_232758_HasThermalProperties()
    {
        var symbol = Record(232758, "SysPanelFamSym", Fields(("1829:m_familyId", 12609), ("1829:m_pParams", 714)), [3913]);
        var family = Record(12609, "Family", SymbolFamilyFields(-2000170, 1, -1), [1809, 496], tailComplete: false);
        var all = new[] { symbol, family }.ToDictionary(e => e.Id);

        var result = CompactParameterLists.GetBuiltinIds("SysPanelFamSym", new ElementListFacts(symbol, -2000170, Plain, id => all.GetValueOrDefault(id)));

        Assert.Null(result.Unknown);
        Assert.Equal([-1152384, -1140422, -1140363, -1140362, -1019017, -1019015, -1019013, -1019001, -1013201, -1010304, -1010302, -1010109,
            -1010108, -1010105, -1010104, -1010103, -1005556, -1005439, -1005437, -1005433, -1005432, -1005431, -1005430, -1002503, -1002502,
            -1002501, -1002500, -1002107, -1002002, -1002001, -1001405, -1001208, -1001207, -1001205], result.Ids);
    }

    [Fact]
    public void StructuralColumnSymbol_ReadsAFamilyParameterTheReaderDoesNotDecode_AndIsUnknown()
    {
        var symbol = Record(420952, "FamilySymbol", Fields(("1829:m_familyId", 412500), ("1829:m_pParams", 714)), [1831]);
        var family = Record(412500, "Family", SymbolFamilyFields(-2001330, 1, -1), [1809]);
        var all = new[] { symbol, family }.ToDictionary(e => e.Id);

        var result = CompactParameterLists.GetBuiltinIds("FamilySymbol", new ElementListFacts(symbol, -2001330, Plain, id => all.GetValueOrDefault(id)));

        Assert.Null(result.Ids);
        Assert.NotNull(result.Unknown);
    }

    [Fact]
    public void ALightingFixtureSymbol_WhoseFamilyParametersMayBeRemoved_IsUnknown()
    {
        var symbol = Record(1, "FamilySymbol", Fields(("1829:m_familyId", 2), ("1829:m_pParams", 714)), [1831], [(-1150137, false)]);
        var family = Record(2, "Family", SymbolFamilyFields(-2001120, 1, -1), [1809]);
        var all = new[] { symbol, family }.ToDictionary(e => e.Id);

        var result = CompactParameterLists.GetBuiltinIds("FamilySymbol", new ElementListFacts(symbol, -2001120, Plain, id => all.GetValueOrDefault(id)));

        Assert.Null(result.Ids);
        Assert.Contains("may remove", result.Unknown);
    }

    [Fact]
    public void RoomElem_WithoutAreaScheme_ListsTheBaseIds()
    {
        var room = Record(1, "RoomElem", Fields(("3471:m_areaSchemeId", -1), ("3471:m_zoneSchemeId", -1)), [1831]);

        var result = CompactParameterLists.GetBuiltinIds("RoomElem", new ElementListFacts(room, -2000160, Plain));

        Assert.Null(result.Unknown);
    }

    // The Orientation of a beam: sketch plane -1 or an arc curve -> 1 (not listed), a straight curve -> 0; arc beams with a work plane list no Orientation
    [Theory]
    [InlineData(-1, "GLine", 1)]
    [InlineData(-1, "GArc", 1)]
    [InlineData(617798, "GLine", 0)]
    [InlineData(627235, "GArc", 1)]
    [InlineData(627235, "GSpline", null)]
    [InlineData(627235, null, 0)]
    public void BeamOrientation_FollowsSketchPlaneAndCurveClass(int plane, string? curveClass, int? expected)
    {
        var fields = new Dictionary<string, int> { ["1794:m_assocSketchPlaneId"] = plane };
        if (curveClass is not null) fields["87:m_pCrv"] = curveClass == "GSpline" ? Tag("GHermiteSpline") : Tag(curveClass);
        var beam = Record(1, "FamilyInstance", fields, []);

        Assert.Equal(expected, new ElementListFacts(beam, -2001320, Plain, _ => null).Get("unmodelled:getMemberIndexToTagMap"));
    }

    // "Level" follows the stored fields except for a work plane based instance without a stored level
    [Theory]
    [InlineData(0, -1, -1, true)]
    [InlineData(0, 311, -1, true)]
    [InlineData(1, 311, -1, true)]
    [InlineData(1, -1, 311, true)]
    [InlineData(1, -1, -1, false)]
    public void Level_FollowsStoredFieldsExceptForWorkPlaneInstancesWithoutOne(int workPlane, int assocLevel, int scheduleLevel, bool decided)
    {
        var instance = Record(1, "FamilyInstance", Fields(("1794:m_workPlaneBased", workPlane), ("37:m_assocLevelId", assocLevel),
            ("1794:m_scheduleOnlyLevelId", scheduleLevel)), []);

        Assert.Equal(decided, CompactFamilyInstanceFacts.LevelFollowsStoredFields(instance));
    }

    [Fact]
    public void Level_IsUnknownWhenAFieldIsNotDecoded()
    {
        var instance = Record(1, "FamilyInstance", Fields(("1794:m_workPlaneBased", 0), ("37:m_assocLevelId", -1)), []);

        Assert.Null(CompactFamilyInstanceFacts.LevelFollowsStoredFields(instance));
    }

    [Fact]
    public void ARecordThatIsNotAFamilyInstance_HasNoInstanceFacts()
    {
        var other = Record(1, "FamilySymbol", Fields(("1794:m_masterSymbolId", 5)), []);

        Assert.Null(new ElementListFacts(other, -1, Plain, _ => null).Get("fi:masterSymbolId"));
    }

    [Fact]
    public void CategoriesVerifiedToCarryPhases_IncludeTheFamilyInstanceCategories()
    {
        Assert.True(CompactParameterLists.CategoryHasPhases(-2001330));
        Assert.True(CompactParameterLists.CategoryHasPhases(-2000171));
        Assert.Null(CompactParameterLists.CategoryHasPhases(-2000127));
    }
}
