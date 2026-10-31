using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Project, shared and family parameters: the ParamElem / ParamDef / ParamBinding capture, the
/// catalog built from it and the lists an element gets from it.</summary>
public class UserParameterCatalogTests
{
    // Exact blocks of Test_2022.rvt: family parameters 16209 (Width, length), 25978 (Baluster Material) and 581326
    // (Horizontal, yes/no, no group), the project parameter 26010 (Occupant) with its binding 26011, and the shared
    // parameter 114978 (A, area, structural).
    private const string Block16209 = "UT8AAFRHdoooAQAAOgsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAAFE/AAD/////WVMCAP/////////////////////8////AAAAAAAAAP////9kAgABAAAA//////ABBQAAAFcAaQBkAHQAaAAvAAAAYQB1AHQAbwBkAGUAcwBrAC4AcABhAHIAYQBtAGUAdABlAHIALgBhAGUAYwAuAHIAZQB2AGkAdAAuAGYAYQBtAGkAbAB5ADoAMQA2ADIAMAA5AC0AMQAuADAALgAwAFu0s/9RPwAAAAABHgAAAGEAdQB0AG8AZABlAHMAawAuAHMAcABlAGMALgBhAGUAYwA6AGwAZQBuAGcAdABoAC0AMQAuADAALgAwAAEAAAAAAAAAAAAAAAAoAQAA";
    private const string Block25978 = "emUAAMpG1rkEAQAAOgsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAAHplAAD/////gGYCAP/////////////////////8////AAAAAAAAAP/////dCgABAAAA//////ABEQAAAEIAYQBsAHUAcwB0AGUAcgAgAE0AYQB0AGUAcgBpAGEAbAAvAAAAYQB1AHQAbwBkAGUAcwBrAC4AcABhAHIAYQBtAGUAdABlAHIALgBhAGUAYwAuAHIAZQB2AGkAdAAuAGYAYQBtAGkAbAB5ADoAMgA1ADkANwA4AC0AMQAuADAALgAwAFe0s/96ZQAAAAABAAAAAP////8AAAAAAAAAAAAEAQAA";
    private const string Block581326 = "zt4IAGpCXi7vAAAAOgsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAAM7eCAD/////Rz8CAP/////////////////////8////AAAAAAAAAP////8yCwABAAAA//////ABCgAAAEgAbwByAGkAegBvAG4AdABhAGwAMAAAAGEAdQB0AG8AZABlAHMAawAuAHAAYQByAGEAbQBlAHQAZQByAC4AYQBlAGMALgByAGUAdgBpAHQALgBmAGEAbQBpAGwAeQA6ADUAOAAxADMAMgA2AC0AMQAuADAALgAwAP/////O3ggAAAABAAAAAAAAAADvAAAA";
    private const string Block26010 = "mmUAAKlD6aryAAAAPAsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAAJplAAD/////////////////////////////////////AAAAAAAAAP////9eAgEAAACbZQAAAQAAAP/////wAQgAAABPAGMAYwB1AHAAYQBuAHQAMAAAAGEAdQB0AG8AZABlAHMAawAuAHAAYQByAGEAbQBlAHQAZQByAC4AYQBlAGMALgByAGUAdgBpAHQALgBwAHIAbwBqAGUAYwB0ADoAMgA2ADAAMQAwAC0AMQAuADAALgAwAFy0s/+aZQAAAAABAAAAAAAAAADyAAAA";
    private const string Block26011 = "m2UAAOsqIIRpAAAASAoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAAJtlAAD/////////////////////////////////////AAAAmmUAAOB64f8BAAAAAQAAAP/////wAQAAAAAAAAAAaQAAAA==";
    private const string Block114978 = "IsEBACBZj49xAQAAOQsAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////40CAQAAACLBAQD////////////////////////////////8////AAAAAAAAAP////9kAgAAAAAtFLF0rh/lS7RfQzKciZjEAAEBAAAA//////ABAQAAAEEATwAAAGEAdQB0AG8AZABlAHMAawAuAHAAYQByAGEAbQBlAHQAZQByAC4AYQBlAGMALgByAGUAdgBpAHQALgBlAHgAdABlAHIAbgBhAGwALgAtADEAOgA3ADQAYgAxADEANAAyAGQAMQBmAGEAZQA0AGIAZQA1AGIANAA1AGYANAAzADMAMgA5AGMAOAA5ADkAOABjADQALQAxAC4AMAAuADAAULSz/yLBAQAAAAEcAAAAYQB1AHQAbwBkAGUAcwBrAC4AcwBwAGUAYwAuAGEAZQBjADoAYQByAGUAYQAtADEALgAwAC4AMAABAAAAAAAAAAAAAAAAcQEAAA==";

    private static TfBlockDeserializer.NativeParams Read(string base64, out ushort cls)
    {
        byte[] block = Convert.FromBase64String(base64);
        cls = BitConverter.ToUInt16(block, 12);
        var np = TfBlockDeserializer.ExtractParams(block, 0, BitConverter.ToUInt32(block, 8), cls);
        Assert.NotNull(np);
        return np!;
    }

    [Theory]
    [InlineData(Block16209, 2874, 612, "Width", -5000101, "autodesk.parameter.aec.revit.family:16209-1.0.0", "autodesk.spec.aec:length-1.0.0", 0)]
    [InlineData(Block25978, 2874, 2781, "Baluster Material", -5000105, "autodesk.parameter.aec.revit.family:25978-1.0.0", null, 0)]
    [InlineData(Block581326, 2874, 2866, "Horizontal", -1, "autodesk.parameter.aec.revit.family:581326-1.0.0", null, 0)]
    [InlineData(Block26010, 2876, 606, "Occupant", -5000100, "autodesk.parameter.aec.revit.project:26010-1.0.0", null, null)]
    [InlineData(Block114978, 2873, 612, "A", -5000112, "autodesk.parameter.aec.revit.external.-1:74b1142d1fae4be5b45f43329c8998c4-1.0.0", "autodesk.spec.aec:area-1.0.0", null)]
    public void ParamElem_GivesCaptionGroupTypeAndSpecOfItsOwnedParamDef(
        string block, int elementClass, int definitionClass, string caption, int group, string typeId, string? spec, int? instance)
    {
        var np = Read(block, out ushort cls);
        Assert.Equal(elementClass, cls);
        Assert.Equal(definitionClass, np.Fields["2870:m_pParamDef.class"]);
        Assert.Equal(caption, np.StringFields["301:m_caption"]);
        Assert.Equal(group, np.Fields["301:m_groupElemId"]);
        Assert.Equal(typeId, np.StringFields["301:m_typeId"]);
        Assert.Equal(spec, np.StringFields.GetValueOrDefault("612:m_specTypeId"));
        Assert.Equal(instance, np.Fields.TryGetValue("2874:m_instanceParam", out int flag) ? flag : (int?)null);
        Assert.Equal(0, np.Fields["301:m_readOnly"]);
        Assert.Equal(1, np.Fields["301:m_userVisible"]);
        Assert.False(np.StringFields.ContainsKey("45:m_typeId"));   // only the two keyed ForgeTypeId strings are kept
    }

    [Fact]
    public void ParamBinding_GivesParameterCategoryAndInstanceFlag()
    {
        var np = Read(Block26011, out ushort cls);
        Assert.Equal(2632, cls);
        Assert.Equal(26010, np.Fields["2632:m_paramId"]);
        Assert.Equal(-2000160, np.Fields["2632:m_categoryId"]);   // OST_Rooms
        Assert.Equal(1, np.Fields["2632:m_elemOrSymbol"]);        // instance binding
    }


    private static TfBlockDeserializer.NativeParams Definition(int id, int definitionClass, string caption, int group,
        string typeId, string? spec = null, int? instance = null)
    {
        var np = new TfBlockDeserializer.NativeParams();
        np.Fields["2870:m_pParamDef.class"] = definitionClass;
        np.StringFields["301:m_caption"] = caption;
        np.Fields["301:m_groupElemId"] = group;
        np.Fields["301:m_paramElemId"] = id;
        np.Fields["301:m_readOnly"] = 0;
        np.Fields["301:m_userVisible"] = 1;
        np.StringFields["301:m_typeId"] = typeId;
        if (spec != null) np.StringFields["612:m_specTypeId"] = spec;
        if (instance != null) np.Fields["2874:m_instanceParam"] = instance.Value;
        return np;
    }

    private static TfBlockDeserializer.NativeParams Binding(int parameter, int category, int instanceOrType)
    {
        var np = new TfBlockDeserializer.NativeParams();
        np.Fields["2632:m_paramId"] = parameter;
        np.Fields["2632:m_categoryId"] = category;
        np.Fields["2632:m_elemOrSymbol"] = instanceOrType;
        return np;
    }

    private static TfBlockDeserializer.NamedParamRecord Named(int id, bool instance, string? text = null, double number = 0, int elementId = 0, int integer = 0)
        => new(id, text, number, elementId, integer, instance, false);

    private const int Family = 100, Symbol = 101, Instance = 102, Room = 103, Wall = 104;

    /// <summary>A family of doors with a type length (1001), an instance yes/no (1002), a type material (1003) and an
    /// instance text (1004); a project text (2001) bound to doors as type and to rooms as instance; a shared number (2002)
    /// bound to doors as instance.</summary>
    private static (UserParameterCatalog Catalog, List<Element> Elements) Sample(bool instanceRecordFor1002 = true, bool symbolRecordFor1002 = true)
    {
        var state = new ParsingState();
        void Add(int id, ushort cls, TfBlockDeserializer.NativeParams np) { state.ElemClassIdx[(uint)id] = cls; state.NativeParams[(uint)id] = np; }
        Add(1001, 2874, Definition(1001, 612, "Leaf Width", -5000101, "autodesk.parameter.aec.revit.family:1001-1.0.0", "autodesk.spec.aec:length-1.0.0", 0));
        Add(1002, 2874, Definition(1002, 2866, "Has Glass", -1, "autodesk.parameter.aec.revit.family:1002-1.0.0", null, 1));
        Add(1003, 2874, Definition(1003, 2781, "Leaf Material", -5000105, "autodesk.parameter.aec.revit.family:1003-1.0.0", null, 0));
        Add(1004, 2874, Definition(1004, 606, "Remark", -5000100, "autodesk.parameter.aec.revit.family:1004-1.0.0", null, 1));
        Add(2001, 2876, Definition(2001, 606, "Occupant", -5000100, "autodesk.parameter.aec.revit.project:2001-1.0.0"));
        Add(2002, 2873, Definition(2002, 612, "Fire Load", -5000112, "autodesk.parameter.aec.revit.external.-1:00000000000000000000000000000001-1.0.0", "autodesk.spec.aec:number-1.0.1"));
        Add(3001, 2632, Binding(2001, -2000023, 2));
        Add(3002, 2632, Binding(2001, -2000160, 1));
        Add(3003, 2632, Binding(2002, -2000023, 1));
        var family = new TfBlockDeserializer.NativeParams();
        family.Fields["1773:m_categoryId"] = -2000023;
        family.NamedParams.Add(Named(1001, false)); family.NamedParams.Add(Named(1002, true));
        family.NamedParams.Add(Named(1003, false)); family.NamedParams.Add(Named(1004, true));
        family.NamedParams.Add(Named(-1005, true));   // a built-in id is not a user parameter
        Add(Family, 1772, family);
        var symbol = new TfBlockDeserializer.NativeParams();
        symbol.Fields["1829:m_familyId"] = Family;
        symbol.NamedParams.Add(Named(1001, false, number: 0.5));
        if (symbolRecordFor1002) symbol.NamedParams.Add(Named(1002, true, integer: 1));
        symbol.NamedParams.Add(Named(1003, false, elementId: 77)); symbol.NamedParams.Add(Named(1004, true, text: "ignored"));
        symbol.Strings.Add((2001, "Office"));
        Add(Symbol, 1829, symbol);
        var instance = new TfBlockDeserializer.NativeParams();
        instance.Fields["m_masterSymbolId"] = Symbol;
        if (instanceRecordFor1002) instance.NamedParams.Add(Named(1002, true, integer: 0));
        instance.NamedParams.Add(Named(1004, true, text: "North door"));
        instance.Doubles.Add((2002, 12.5));
        Add(Instance, 1794, instance);
        var room = new TfBlockDeserializer.NativeParams();
        room.Strings.Add((2001, "Kitchen"));
        Add(Room, 3471, room);
        Add(Wall, 3524, new TfBlockDeserializer.NativeParams());
        var elements = new List<Element>
        {
            new() { Id = Family, SerializedOwnElementClassId = 1772 },
            new() { Id = Symbol, SerializedOwnElementClassId = 1829 },
            new() { Id = Instance, SerializedOwnElementClassId = 1794 },
            new() { Id = Room, SerializedOwnElementClassId = 3471, CategoryId = -2000160 },
            new() { Id = Wall, SerializedOwnElementClassId = 3524, CategoryId = -2000011, NativeHeaderCategoryId = -2000011 },
        };
        return (UserParameterCatalog.Build(state), elements);
    }

    [Fact]
    public void Build_ReadsDefinitionsAndBindings()
    {
        var (catalog, _) = Sample();
        Assert.Equal(6, catalog.Definitions.Count);
        Assert.True(catalog.TryGetDefinition(1001, out var width));
        Assert.Equal(("Leaf Width", -5000101, StorageType.Double, "autodesk.spec.aec:length-1.0.0", false),
            (width.Caption, width.GroupId, width.Storage, width.Spec, width.Instance));
        Assert.True(catalog.TryGetDefinition(1002, out var glass));
        Assert.Equal((StorageType.Integer, "autodesk.spec:spec.bool-1.0.0", true, true), (glass.Storage, glass.Spec, glass.IsYesNo, glass.Instance));
        Assert.True(catalog.TryGetDefinition(1003, out var material));
        Assert.Equal((StorageType.ElementId, "autodesk.spec.aec:material-1.0.0"), (material.Storage, material.Spec));
        Assert.Equal(new[] { new UserParameterBinding(-2000023, false), new UserParameterBinding(-2000160, true) }, catalog.BindingsOf(2001));
        Assert.Equal(new[] { 2002 }, catalog.BoundTo(-2000023, true));
        Assert.Equal(new[] { 2001 }, catalog.BoundTo(-2000023, false));
        Assert.Empty(catalog.BindingsOf(1001));
    }

    [Fact]
    public void Symbol_ListsTheTypeParametersOfItsFamilyAndTheTypeBindingsOfItsCategory()
    {
        var (catalog, elements) = Sample();
        var entries = catalog.EntriesOf(elements[1]);
        Assert.Equal(new[] { 1001, 1003, 2001 }, entries.Select(e => e.Definition.Id));
        Assert.Equal(0.5, entries[0].Value!.Value.Number);
        Assert.Equal(77, entries[1].Value!.Value.ElementId);
        Assert.Equal("Office", entries[2].Value!.Value.Text);
    }

    [Fact]
    public void Instance_ListsTheInstanceParametersOfTheFamilyOfItsSymbolAndTheInstanceBindings()
    {
        var (catalog, elements) = Sample();
        var entries = catalog.EntriesOf(elements[2]);
        Assert.Equal(new[] { 1002, 1004, 2002 }, entries.Select(e => e.Definition.Id));
        Assert.Equal(0, entries[0].Value!.Value.Integer);                  // the instance's own record, not the symbol's
        Assert.Equal("North door", entries[1].Value!.Value.Text);
        Assert.Equal(12.5, entries[2].Value!.Value.Number);               // stored value of the bound parameter
    }

    [Fact]
    public void FamilyParameterWithoutAStoredRecordIsLeftOut()
    {
        var (catalog, elements) = Sample(instanceRecordFor1002: false, symbolRecordFor1002: false);
        Assert.Equal(new[] { 1004, 2002 }, catalog.EntriesOf(elements[2]).Select(e => e.Definition.Id));
        Assert.Equal(new[] { 1002, 1004, 2002 }, catalog.EntriesOf(elements[2], includeUnvalidated: true).Select(e => e.Definition.Id));
    }

    [Fact]
    public void InstanceWithoutAValueOfItsOwnShowsTheValueOfItsSymbol()
    {
        var (catalog, elements) = Sample(instanceRecordFor1002: false);
        var entry = catalog.EntriesOf(elements[2]).Single(e => e.Definition.Id == 1002);
        Assert.Equal(1, entry.Value!.Value.Integer);                       // the symbol's record
    }

    [Fact]
    public void InstanceWithoutAKnownSymbolListsNothing_AndBoundParameterWithoutAStoredValueHasNone()
    {
        var (catalog, _) = Sample();
        Assert.Empty(catalog.EntriesOf(new Element { Id = 999, SerializedOwnElementClassId = 1794 }));
        var bound = catalog.EntriesOf(new Element { Id = 555, SerializedOwnElementClassId = 3471, CategoryId = -2000160 });
        Assert.Equal(2001, Assert.Single(bound).Definition.Id);
        Assert.Null(bound[0].Value);
    }

    [Fact]
    public void Room_ListsTheInstanceBindingsOfItsCategory_AndAWallOfAnUnadmittedCategoryNothing()
    {
        var (catalog, elements) = Sample();
        var entries = catalog.EntriesOf(elements[3]);
        Assert.Equal("Kitchen", Assert.Single(entries).Value!.Value.Text);
        Assert.Empty(catalog.EntriesOf(elements[4]));
        Assert.Empty(catalog.EntriesOf(new Element { Id = 700, SerializedOwnElementClassId = 2050, CategoryId = -2000160 }));   // GStyleElem: its category is a default
    }

    [Fact]
    public void InstanceCategory_IsTheFamilyCategory_ElseTheReaderCategoryOfAKnownFamily_ElseNone()
    {
        var (catalog, elements) = Sample();
        Assert.Equal(-2000023, catalog.BoundCategoryOf(new Element { Id = Instance, SerializedOwnElementClassId = 1794, CategoryId = -2000999 }));   // family category wins
        // a family whose category was not captured: the category the reader gives the instance
        var state = new ParsingState();
        var symbol = new TfBlockDeserializer.NativeParams(); symbol.Fields["1829:m_familyId"] = 900;
        state.ElemClassIdx[901] = 1829; state.NativeParams[901] = symbol;
        var instance = new TfBlockDeserializer.NativeParams(); instance.Fields["m_masterSymbolId"] = 901;
        state.ElemClassIdx[902] = 1794; state.NativeParams[902] = instance;
        var uncategorised = UserParameterCatalog.Build(state);
        Assert.Equal(-2000014, uncategorised.BoundCategoryOf(new Element { Id = 902, SerializedOwnElementClassId = 1794, CategoryId = -2000014 }));
        Assert.Null(uncategorised.BoundCategoryOf(new Element { Id = 903, SerializedOwnElementClassId = 1794, CategoryId = -2000014 }));   // no symbol: no family
    }

    [Theory]
    [InlineData(-2000011)]   // Walls
    [InlineData(-2000170)]   // Curtain Panels: a wall used as a panel of a curtain wall
    [InlineData(-2003500)]   // Stacked Walls (the reader gives every wall the category Walls)
    [InlineData(null)]       // no header category decoded: not read
    public void WallCategoryIsTheHeaderCategory(int? category)
    {
        var (catalog, _) = Sample();
        Assert.Equal(category, catalog.BoundCategoryOf(
            new Element { Id = Wall, SerializedOwnElementClassId = 3524, CategoryId = -2000011, NativeHeaderCategoryId = category }));
    }

    [Fact]
    public void WallListsTheParametersBoundToItsHeaderCategory()
    {
        var state = new ParsingState();
        void Add(int id, ushort cls, TfBlockDeserializer.NativeParams np) { state.ElemClassIdx[(uint)id] = cls; state.NativeParams[(uint)id] = np; }
        Add(2001, 2876, Definition(2001, 606, "On Walls", -5000100, "autodesk.parameter.aec.revit.project:2001-1.0.0"));
        Add(2002, 2876, Definition(2002, 606, "On Panels", -5000100, "autodesk.parameter.aec.revit.project:2002-1.0.0"));
        Add(3001, 2632, Binding(2001, -2000011, 1));
        Add(3002, 2632, Binding(2002, -2000170, 1));
        var catalog = UserParameterCatalog.Build(state);
        Element Wall(int category) => new() { Id = 104, SerializedOwnElementClassId = 3524, CategoryId = -2000011, NativeHeaderCategoryId = category };
        Assert.Equal(2001, Assert.Single(catalog.EntriesOf(Wall(-2000011))).Definition.Id);
        Assert.Equal(2002, Assert.Single(catalog.EntriesOf(Wall(-2000170))).Definition.Id);
        Assert.Empty(catalog.EntriesOf(Wall(-2003500)));
    }

    // A section (Section, ElemClass 3545) stores no parameter value of its own: the export prints those of the view it stands for
    // (Viewer.m_dbViewId). A value the view does not store is an empty text only when the stores of the view were read completely.
    private const int SectionId = 700, SectionViewId = 701, SectionClass = 3545, ViewSectionClass = 1154;

    private static (UserParameterCatalog Catalog, Element Section) SectionSample(bool viewStoresComplete, bool viewKnown = true)
    {
        var state = new ParsingState();
        void Add(int id, ushort cls, TfBlockDeserializer.NativeParams np) { state.ElemClassIdx[(uint)id] = cls; state.NativeParams[(uint)id] = np; }
        Add(2001, 2876, Definition(2001, 606, "Browser Level 1", -5000100, "autodesk.parameter.aec.revit.project:2001-1.0.0"));
        Add(2002, 2876, Definition(2002, 606, "Browser Level 2", -5000100, "autodesk.parameter.aec.revit.project:2002-1.0.0"));
        Add(3001, 2632, Binding(2001, -2000279, 1));
        Add(3002, 2632, Binding(2002, -2000279, 1));
        var section = new TfBlockDeserializer.NativeParams();
        if (viewKnown) section.Fields[WideNativeParams.ViewerViewKey] = SectionViewId;
        section.RootParameterStoresComplete = true;
        Add(SectionId, SectionClass, section);
        var view = new TfBlockDeserializer.NativeParams { RootParameterStoresComplete = viewStoresComplete };
        view.Strings.Add((2001, "00_GENERAL"));
        Add(SectionViewId, ViewSectionClass, view);
        return (UserParameterCatalog.Build(state),
            new Element { Id = SectionId, SerializedOwnElementClassId = SectionClass, CategoryId = -2000279, NativeParameterStoresComplete = true });
    }

    [Fact]
    public void Section_ListsTheValuesOfItsView_AndNoValueWhereTheViewStoresNone()
    {
        var (catalog, section) = SectionSample(viewStoresComplete: true);
        Assert.True(catalog.ValuesKnown(section));
        var entries = catalog.EntriesOf(section);
        Assert.Equal(new[] { 2001, 2002 }, entries.Select(e => e.Definition.Id));
        Assert.Equal("00_GENERAL", entries[0].Value!.Value.Text);
        Assert.Null(entries[1].Value);
    }

    [Theory]
    [InlineData(false, true)]    // the stores of the view were not read completely: its values are not decided
    [InlineData(true, false)]    // the section names no view
    public void Section_WithoutAViewWhoseStoresAreComplete_HasNoKnownValues(bool viewStoresComplete, bool viewKnown)
    {
        var (catalog, section) = SectionSample(viewStoresComplete, viewKnown);
        Assert.False(catalog.ValuesKnown(section));
        Assert.All(catalog.EntriesOf(section), e => Assert.Null(e.Value));
    }

    [Fact]
    public void ValuesKnown_IsTrueForAnyOtherClass()
    {
        var (catalog, _) = Sample();
        Assert.All(new[] { Family, Symbol, Instance, Room }, id => Assert.True(catalog.ValuesKnown(new Element { Id = id, SerializedOwnElementClassId = 3471 })));
    }

    [Theory]
    [InlineData(-5000100, "Identity Data")]
    [InlineData(-5000101, "Dimensions")]
    [InlineData(-5000130, "Electrical")]
    [InlineData(-5000124, "Electrical - Lighting")]
    [InlineData(-1, null)]
    [InlineData(123456, null)]
    public void GroupLabel_IsTheEnglishLabelOrNullForInvalidGroup(int group, string? label)
        => Assert.Equal(label, UserParameterCatalog.GroupLabel(group));

    // The group of a definition of Revit 2024 and later is a ForgeTypeId string (ParamDef.m_groupTypeId): the label the export prints for it,
    // "Invalid group" for an empty string, none for a string the corpus does not confirm.
    [Theory]
    [InlineData("autodesk.parameter.group:dimensions-1.0.0", "Dimensions")]
    [InlineData("autodesk.parameter.group:identityData-1.0.0", "Identity Data")]
    [InlineData("autodesk.parameter.group:electrical-1.0.0", "Electrical")]
    [InlineData("autodesk.parameter.group:electricalEngineering-1.0.0", "Electrical Engineering")]
    [InlineData("autodesk.parameter.group:mechanicalAirflow-1.0.0", "Mechanical - Flow")]
    [InlineData("autodesk.parameter.group:greenBuilding-1.0.0", "Green Building Properties")]
    [InlineData("", "Invalid group")]
    [InlineData("autodesk.parameter.group:analysisResults-1.0.0", "Analysis Results")]
    [InlineData("autodesk.parameter.group:rebarArray-1.0.0", "Rebar Set")]
    [InlineData("autodesk.parameter.group:overallLegend-1.0.0", "Overall Legend")]
    [InlineData("autodesk.parameter.group:visibility-1.0.0", "Visibility")]
    [InlineData("autodesk.parameter.group:segmentsFittings-1.0.0", "Segments and Fittings")]
    [InlineData("autodesk.parameter.group:ifc-1.0.0", "IFC Parameters")]
    [InlineData("autodesk.revit.group:structuralSectionGeometry-1.0.0", "Structural Section Geometry")]
    [InlineData("autodesk.parameter.group:primaryEnd-1.0.0", "Primary End")]
    [InlineData("autodesk.parameter.group:secondaryEnd-1.0.0", "Secondary End")]
    [InlineData("autodesk.parameter.group:energyAnalysis-1.0.0", "Energy Analysis")]
    [InlineData("autodesk.parameter.group:couplerArray-1.0.0", "Set")]
    [InlineData("autodesk.parameter.group:mechanicalLoads-1.0.0", "Mechanical - Loads")]
    [InlineData("autodesk.parameter.group:structuralAnalysis-1.0.0", "Structural Analysis")]
    [InlineData("autodesk.parameter.group:slabShapeEdit-1.0.0", "Slab Shape Edit")]
    [InlineData("autodesk.parameter.group:electricalCircuiting-1.0.0", "Electrical - Circuiting")]
    [InlineData("autodesk.parameter.group:fireProtection-1.0.0", null)]
    [InlineData("autodesk.parameter.group:unknown-1.0.0", null)]
    public void GroupLabelOf_AForgeTypeIdGroupHasItsValidatedLabel(string groupTypeId, string? label)
        => Assert.Equal(label, UserParameterCatalog.GroupLabelOf(
            new UserParameterDefinition(1, 2874, 606, "P", -1, null, null, false, true, null, groupTypeId)));

    [Theory]
    [InlineData(-5000101, "Dimensions")]
    [InlineData(-1, "Invalid group")]
    public void GroupLabelOf_ANumberGroupKeepsTheOlderRule(int group, string label)
        => Assert.Equal(label, UserParameterCatalog.GroupLabelOf(
            new UserParameterDefinition(1, 2874, 606, "P", group, null, null, false, true, null)));
    private static UserParameterCatalog Catalog(Action<ParsingState, Action<int, ushort, TfBlockDeserializer.NativeParams>> fill)
    {
        var state = new ParsingState();
        fill(state, (id, cls, np) => { state.ElemClassIdx[(uint)id] = cls; state.NativeParams[(uint)id] = np; });
        return UserParameterCatalog.Build(state);
    }

    [Fact]
    public void ReferenceTableStoredValuesDoNotProveWhichParametersAreListed()
    {
        const ushort refTableElem = 3398;
        var catalog = Catalog((_, add) =>
        {
            add(3001, 2876, Definition(3001, 606, "Hinge Set", -1, "autodesk.parameter.aec.revit.project:3001-1.0.0"));
            add(3002, 2876, Definition(3002, 2866, "Hold Open", -1, "autodesk.parameter.aec.revit.project:3002-1.0.0"));
            add(3003, 2876, Definition(3003, 606, "Stop", -1, "autodesk.parameter.aec.revit.project:3003-1.0.0"));
            add(3004, 2632, Binding(3003, -2000023, 1));
            var row = new TfBlockDeserializer.NativeParams();
            row.Strings.Add((3001, "3 Butt"));
            row.Ints.Add((3002, 0));
            add(900, refTableElem, row);
        });
        var entries = catalog.EntriesOf(new Element { Id = 900, SerializedOwnElementClassId = refTableElem });
        Assert.Empty(entries);
    }

    [Fact]
    public void PipeCurve_IsBoundByItsHeaderCategory_NotByTheReadersCategory()
    {
        const ushort rbsPipeCurve = 3210;
        var catalog = Catalog((_, add) =>
        {
            add(3001, 2876, Definition(3001, 606, "Pipe Note", -5000100, "autodesk.parameter.aec.revit.project:3001-1.0.0"));
            add(3002, 2632, Binding(3001, -2008044, 1));
        });
        var pipe = new Element { Id = 901, SerializedOwnElementClassId = rbsPipeCurve, CategoryId = -2000011, NativeHeaderCategoryId = -2008044 };
        Assert.Equal(-2008044, catalog.BoundCategoryOf(pipe));
        Assert.Equal(3001, Assert.Single(catalog.EntriesOf(pipe)).Definition.Id);
        Assert.Null(catalog.BoundCategoryOf(new Element { Id = 902, SerializedOwnElementClassId = rbsPipeCurve, CategoryId = -2008044 }));
    }

    [Theory]
    [InlineData(4122)]
    [InlineData(2248)]
    public void ViewMarker_PrintsTheValuesOfTheViewItStandsFor(ushort markerClass)
    {
        var catalog = Catalog((_, add) =>
        {
            add(3001, 2876, Definition(3001, 606, "Browser Level", -5000100, "autodesk.parameter.aec.revit.project:3001-1.0.0"));
            add(3002, 2632, Binding(3001, -2000280, 1));
            var marker = new TfBlockDeserializer.NativeParams();
            marker.Fields[WideNativeParams.ViewerViewKey] = 911;
            add(910, markerClass, marker);
            var view = new TfBlockDeserializer.NativeParams { RootParameterStoresComplete = true };
            view.Strings.Add((3001, "00_ALLGEMEIN"));
            add(911, 1016, view);
        });
        var element = new Element { Id = 910, SerializedOwnElementClassId = markerClass, CategoryId = -2000280, NativeHeaderCategoryId = -2000280 };
        Assert.True(catalog.ValuesKnown(element));
        Assert.Equal("00_ALLGEMEIN", Assert.Single(catalog.EntriesOf(element)).Value!.Value.Text);
        Assert.False(catalog.ValuesKnown(new Element { Id = 912, SerializedOwnElementClassId = markerClass }));
    }


    internal static readonly Lazy<RevitFileInfo> Test2022 = new(() =>
        new RvtFileReader().Read(TestFileFixture.FindTestFile("Test_2022.rvt")));

    [SkippableFact]
    public void Test2022_CatalogHoldsEveryDefinitionAndBindingOfTheFile()
    {
        var catalog = Test2022.Value.UserParameters!;
        Assert.NotNull(catalog);
        Assert.True(catalog.TryGetDefinition(26010, out var occupant));
        Assert.Equal(("Occupant", -5000100, StorageType.String), (occupant.Caption, occupant.GroupId, occupant.Storage));
        Assert.Equal(new[] { new UserParameterBinding(-2000160, true) }, catalog.BindingsOf(26010));
        Assert.Equal(new[] { new UserParameterBinding(-2000700, true) }, catalog.BindingsOf(198377));
        Assert.True(catalog.TryGetDefinition(114978, out var shared));
        Assert.Equal(("A", "autodesk.spec.aec:area-1.0.0"), (shared.Caption, shared.Spec));
        Assert.True(catalog.TryGetDefinition(581326, out var horizontal));
        Assert.Equal(-1, horizontal.GroupId);
        Assert.Null(UserParameterCatalog.GroupLabel(horizontal.GroupId));
    }

    [Fact]
    public void ElementGroupRetainsBoundParameters()
    {
        var (catalog, _) = Sample();
        var group = new Element { Id = Room, SerializedOwnElementClassId = 1322, CategoryId = -2000160 };
        Assert.Equal("Kitchen", Assert.Single(catalog.EntriesOf(group)).Value!.Value.Text);
        group.SerializedFamilyId = 500;
        Assert.Empty(catalog.EntriesOf(group));
    }


}
