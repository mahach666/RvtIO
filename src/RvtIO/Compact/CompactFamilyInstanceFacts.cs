using RvtIO.Model;
using RvtIO.Parsing;

namespace RvtIO.Compact;

/// <summary>
/// Facts that decide the parameter list of a family instance and of a family symbol:
/// fields of the instance record, of its symbol and of the symbol's family, and the predicates
/// derived from them. A fact is null (unknown) whenever a record or a field it needs was not decoded.
/// </summary>
internal static class CompactFamilyInstanceFacts
{
    private const string FamilyInstanceClass = "FamilyInstance";
    private const int StructuralColumnCategory = -2001330, StructuralFoundationCategory = -2001300, StructuralFramingCategory = -2001320;
    private const int ArchitecturalColumnCategory = -2000100, CurtainPanelsCategory = -2000171, ModelGroupsCategory = -2000127;

    private static readonly Lazy<Dictionary<string, int>> ClassTags = new(() =>
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (index, schema) in TfBlockDeserializer.Schemas) map[schema.Name] = index;
        return map;
    });

    // Subclasses of FamInstUserData whose getListParams (vtable slot 1) is not an empty function, numbered from 1: structural footing,
    // mass, structural column, structural framing. Every other subclass (and no user data) is kind 0: it lists nothing.
    private static readonly string[] UserDataKinds = ["StructFootingUserData", "MassInstUserData", "StructColumnUserData", "StructFramUserData"];

    private static readonly string[] SymbolClasses = ["FamilySymbol", "SysMullionFamSym", "SysPanelFamSym"];

    internal static bool Applies(Element element) => ElementListFacts.RecordClassName(element) == FamilyInstanceClass;

    private static bool IsSymbol(Element element) => ElementListFacts.RecordClassName(element) is { } name && Array.IndexOf(SymbolClasses, name) >= 0;

    private static int? Field(Element e, string key) =>
        e.NativeListFields is { } fields && fields.TryGetValue(key, out int value) ? value : null;

    private static int? Tag(string className) => ClassTags.Value.TryGetValue(className, out int tag) ? tag : null;

    private static int? Flag(bool value) => value ? 1 : 0;

    private static int? NullIf(int? value, bool isNull) => isNull ? 0 : value is null ? null : 1;

    /// <summary>The master symbol and its family (null when a record of the chain is not in the document).</summary>
    private static (Element? Symbol, Element? Family) Chain(Element instance, Func<int, Element?>? resolve)
    {
        // a family symbol is its own symbol
        if (IsSymbol(instance))
            return (instance, resolve is not null && Field(instance, TfClassNames.Key("FamilySymbol", "m_familyId")) is int own && own != -1 ? resolve(own) : null);
        if (resolve is null || Field(instance, TfClassNames.Key("FamilyInstance", "m_masterSymbolId")) is not int symbolId || symbolId == -1) return (null, null);
        var symbol = resolve(symbolId);
        if (symbol is null || Field(symbol, TfClassNames.Key("FamilySymbol", "m_familyId")) is not int familyId || familyId == -1) return (symbol, null);
        return (symbol, resolve(familyId));
    }

    /// <summary>Category of the family instance's symbol: the symbol takes the category
    /// of its family.</summary>
    internal static int? SymbolCategory(Element instance, Func<int, Element?>? resolve) =>
        Chain(instance, resolve).Family is { } family ? Field(family, TfClassNames.Key("FamilyBase", "m_categoryId")) : null;

    /// <summary>True when the "Level" of a family instance (SCHEDULE_LEVEL_PARAM) follows from the stored fields: the association level, else
    /// the schedule-only level, else "None" for an instance that is not work plane based. A work plane based instance with neither stored
    /// level gets its level from the work plane or host, which the reader does not hold: false there. Null when a field is not decoded.</summary>
    internal static bool? LevelFollowsStoredFields(Element e)
    {
        if (!Applies(e)
            || Field(e, TfClassNames.Key("FamilyInstance", "m_workPlaneBased")) is not int workPlane
            || Field(e, TfClassNames.Key("Element", "m_assocLevelId")) is not int assoc
            || Field(e, TfClassNames.Key("FamilyInstance", "m_scheduleOnlyLevelId")) is not int schedule) return null;
        return workPlane == 0 || assoc > 0 || schedule > 0;
    }

    internal static int? Get(string fact, Element e, Func<int, Element?>? resolve)
    {
        if (fact.StartsWith("fs:", StringComparison.Ordinal)) return IsSymbol(e) ? SymbolFact(fact, e, resolve) : null;
        if (!Applies(e) && !(IsSymbol(e) && fact.StartsWith("fi:fam", StringComparison.Ordinal))) return null;
        switch (fact)
        {
            case "fi:masterSymbolId": return Field(e, TfClassNames.Key("FamilyInstance", "m_masterSymbolId"));
            case "fi:useOffsetPos": return Field(e, TfClassNames.Key("FamilyInstance", "m_useOffsetPos"));
            case "fi:workPlaneBased": return Field(e, TfClassNames.Key("FamilyInstance", "m_workPlaneBased"));
            case "fi:assocSketchPlaneId": return Field(e, TfClassNames.Key("FamilyInstance", "m_assocSketchPlaneId"));
            case "fi:scheduleOnlyLevelId": return Field(e, TfClassNames.Key("FamilyInstance", "m_scheduleOnlyLevelId"));
            case "fi:assocLevelId": return Field(e, TfClassNames.Key("Element", "m_assocLevelId"));
            case "fi:isDummy": return Field(e, TfClassNames.Key("Element", "m_dummy"));
            case "fi:hasCurveDriver": return Field(e, TfClassNames.Key("FamilyInstance", "m_pCurveDriver")) is int drv ? Flag(drv != 0) : null;
            case "fi:driverIsStructColumn": return DriverIs(e, "StructColumnCurveDriver");
            case "fi:driverIsGenFamInst": return DriverIs(e, "GenFamInstCurveDriver");
            case "fi:driverIsFamInst": return DriverIs(e, "FamInstCurveDriver");
            case "fi:hasInstanceParams": return Field(e, TfClassNames.Key("FamilyInstance", "m_pInstParams")) is int ip ? Flag(ip != 0) : null;
            case "fi:hasUserData": return Field(e, TfClassNames.Key("FamilyInstance", "m_oFamInstUserData")) is int ud ? Flag(ud != 0) : null;
            case "fi:userDataKind":
                return Field(e, TfClassNames.Key("FamilyInstance", "m_oFamInstUserData")) is int udTag
                    ? Array.FindIndex(UserDataKinds, name => Tag(name) == udTag) + 1 : null;
            case "fi:hasSpec": return Field(e, TfClassNames.Key("FamilyInstance", "m_oFamInstSpec")) is int spec ? Flag(spec != 0) : null;
            case "fi:specClass": return Field(e, TfClassNames.Key("FamilyInstance", "m_oFamInstSpec")) is int specTag && specTag > 0 ? specTag : null;
            case "fi:hasDesignPropManager": return Field(e, TfClassNames.Key("FamilyInstance", "m_pDesignPropManager")) is int dpm ? Flag(dpm != 0) : null;
            // the engine reads the length of the reference array through its header: the array length plus four
            case "fi:refsHeader": return Field(e, TfClassNames.Key("FamilyInstance", "m_refs#")) is int refs ? refs + 4 : null;
            case "fi:isAdaptiveComponent":
                if (Field(e, TfClassNames.Key("FamilyInstance", "m_refs#")) is not int refCount) return null;
                if (refCount == 0) return 0;
                return Field(e, TfClassNames.Key("FamilyInstance", "m_refs@0")) is int first && Tag("FlexGeomInstFamRef") is int flex ? Flag(first == flex) : null;
            case "fi:isSlantedColumn":
                // only a structural column curve driver makes a column slanted; the rest is decided by that driver's data
                return Field(e, TfClassNames.Key("FamilyInstance", "m_pCurveDriver")) is int driver && (driver == 0 || (driver > 0 && driver != Tag("StructColumnCurveDriver"))) ? 0 : null;
            // A family instance: found / the attachment object (non-zero when the end has one)
            case "fi:attachFound0": case "fi:attachObject0": return AttachmentPresent(e, 0);
            case "fi:attachFound1": case "fi:attachObject1": return AttachmentPresent(e, 1);
            case "fi:attachDormant0": return AttachmentPresent(e, 0) == 1 ? Field(e, TfClassNames.Key("ColumnAttachment", "dormant@0")) : null;
            case "fi:attachDormant1": return AttachmentPresent(e, 1) == 1 ? Field(e, TfClassNames.Key("ColumnAttachment", "dormant@1")) : null;
            case "fi:coverSettingsActive": return Field(e, TfClassNames.Key("CoverSettingsCell", "m_active"));
            // The two calls the rules name getMemberIndexToTagMap (one code body behind several accessors of the curve driver) decide
            // whether a beam lists "Orientation" (-1001573). A beam lists it when it lists "Work Plane" (-1001380), that is
            // when it has an associated sketch plane (m_assocSketchPlaneId not -1). The exceptions are beams whose curve driver holds an
            // arc (CurveDriver.m_pCrv is a GArc; the others hold a GLine): an arc beam lists no Orientation. A curve of another class is
            // not observed.
            case "unmodelled:getMemberIndexToTagMap": case "unmodelled:getMemberIndexToTagMap#2":
            {
                if (Field(e, TfClassNames.Key("FamilyInstance", "m_assocSketchPlaneId")) is not int plane) return null;
                if (plane == -1) return 1;
                if (Field(e, TfClassNames.Key("CurveDriver", "m_pCrv")) is not int curve || curve == 0) return 0;
                return curve == Tag("GLine") ? 0 : curve == Tag("GArc") ? 1 : null;
            }
            case "fi:canMoveElevation": return CanMoveElevation(e, resolve);
            // framing user data (CompactFramingUserData): 1 when a coped end stores a coping distance (-1 = none), 1 when an end of the
            // member is attached to another element (FamInstStructuralAttachment.m_attachmentType of an end is not 0)
            case "fi:framingCoping": return e.FramingCopingDistance is double coping ? Flag(coping >= 0) : null;
            case "fi:framingAttachment":
                return Field(e, TfClassNames.Key("FamInstStructuralAttachment", "m_attachmentType@0")) is int start
                    && Field(e, TfClassNames.Key("FamInstStructuralAttachment", "m_attachmentType@1")) is int end ? Flag(start != 0 || end != 0) : null;
        }
        var (symbol, family) = Chain(e, resolve);
        switch (fact)
        {
            case "fi:symbolHasParams": return symbol is not null && Field(symbol, TfClassNames.Key("FamilySymbol", "m_pParams")) is int sp ? Flag(sp != 0) : null;
            case "fi:isStructColumn": return SymbolCategory(e, resolve) is int c1 ? Flag(c1 == StructuralColumnCategory) : null;
            case "fi:isArchitectureColumn": return SymbolCategory(e, resolve) is int c2 ? Flag(c2 == ArchitecturalColumnCategory) : null;
            case "fi:structTypeFromSymbol":
                if (SymbolCategory(e, resolve) is not int category) return null;
                switch (category)
                {
                    case StructuralColumnCategory: return 3;
                    case StructuralFoundationCategory: return 4;
                    case StructuralFramingCategory:
                        if (family is null) return null;
                        return Field(family, TfClassNames.Key("FamilyBase", "m_isCrvDriven")) switch { 0 => 4, 1 => Field(e, TfClassNames.Key("FamilyInstance", "m_structType")), _ => null };
                    default: return 0;
                }
        }
        if (family is null) return null;
        int? Fam(string key) => Field(family, key);
        switch (fact)
        {
            case "fi:famIs2d": return Fam(TfClassNames.Key("FamilyBase", "m_is2d"));
            case "fi:famInPlace": return Fam(TfClassNames.Key("FamilyBase", "m_inPlace"));
            case "fi:famIsModel": return Fam(TfClassNames.Key("FamilyBase", "m_isModel"));
            case "fi:famIsCurtainPanel": return Fam(TfClassNames.Key("FamilyBase", "m_isCurtainPanel"));
            case "fi:famIsWorkPlaneBased": return Fam(TfClassNames.Key("FamilyBase", "m_isWorkPlaneBased"));
            case "fi:famStructMaterialType": return Fam(TfClassNames.Key("FamilyBase", "m_eStructMaterialType"));
            case "fi:famLightSourceBit1": return Fam(TfClassNames.Key("FamilyBase", "m_lightSourceDefBits")) is int bits ? bits & 1 : null;
            case "fi:famLightSourceBits": return Fam(TfClassNames.Key("FamilyBase", "m_lightSourceDefBits"));
            case "fi:famPartType": return Fam(TfClassNames.Key("FamilyBase", "m_partType"));
            case "fi:famSymbolicRepType": return Fam(TfClassNames.Key("FamilyBase", "m_eSymbolicRepType"));
            case "fi:famElevationFixed": return Fam(TfClassNames.Key("Family", "m_elevationFixed"));
            case "fi:famHostType": return Fam(TfClassNames.Key("Family", "m_refTypeIds@0"));
            case "fi:famIsStandalone": return Standalone(family);
            case "fi:famMayHaveFreeHost": return MayHaveFreeHost(family);
            case "fi:isFreeFamilySymbol":
                if (Fam(TfClassNames.Key("FamilyBase", "m_isCurtainPanel")) is not int curtain || Fam(TfClassNames.Key("FamilyBase", "m_categoryId")) is not int fcat
                    || Fam(TfClassNames.Key("FamilyBase", "m_isCrvDriven")) is not int crvDriven || Fam(TfClassNames.Key("Family", "m_refTypeIds@0")) is not int host) return null;
                return Flag(!(curtain != 0 || fcat == CurtainPanelsCategory || (fcat == StructuralFramingCategory && crvDriven != 0)
                              || (host != 0 && host != 7)));
        }
        return null;
    }

    // A family symbol: the symbol's family is one of the categories
    // -2000014, -2000023, -2000170 (a symbol without family has none)
    private static int? SymbolFact(string fact, Element symbol, Func<int, Element?>? resolve)
    {
        switch (fact)
        {
            case "fs:familyId": return Field(symbol, TfClassNames.Key("FamilySymbol", "m_familyId"));
            case "fs:hasParams": return Field(symbol, TfClassNames.Key("FamilySymbol", "m_pParams")) is int p ? Flag(p != 0) : null;
            case "fs:hasThermalProperties":
                if (Field(symbol, TfClassNames.Key("FamilySymbol", "m_familyId")) is not int familyId) return null;
                if (familyId == -1) return 0;
                return Chain(symbol, resolve).Family is { } family && Field(family, TfClassNames.Key("FamilyBase", "m_categoryId")) is int category
                    ? Flag(category is -2000014 or -2000023 or -2000170) : null;
        }
        return null;
    }

    private static int? DriverIs(Element e, string className) =>
        Field(e, TfClassNames.Key("FamilyInstance", "m_pCurveDriver")) is int drv && drv > 0 && Tag(className) is int tag ? Flag(drv == tag) : null;

    /// <summary>The cell list holds no ColumnAttachment (class 699): both ends have no attachment.</summary>
    private static bool NoColumnAttachment(Element e)
    {
        if (e.NativeCellListTag == 0) return true;
        return e.NativeCellListTag is not null && e.NativeCellClasses is { } cells && Tag("ColumnAttachment") is int tag && Array.IndexOf(cells, tag) < 0;
    }

    /// <summary>1 when the column end (0 base, 1 top) has an attachment, 0 when it has none, null when the cells were not read completely.</summary>
    private static int? AttachmentPresent(Element e, int end)
    {
        if (NoColumnAttachment(e)) return 0;
        if (!e.NativeTailWalkComplete) return null;
        return Field(e, TfClassNames.Key("ColumnAttachment", "present@") + end) == 1 ? 1 : 0;
    }

    // A family instance: no for a family with fixed elevation; yes when the family is not in place; for an in-place
    // family that is not a 3d model: no. An in-place 3d model family depends on the symbol's move restrictions (not decoded: unknown).
    private static int? CanMoveElevation(Element e, Func<int, Element?>? resolve)
    {
        var (symbol, family) = Chain(e, resolve);
        if (symbol is null || family is null || Field(family, TfClassNames.Key("Family", "m_elevationFixed")) is not int fixedElevation) return null;
        if (fixedElevation != 0) return 0;
        if (Field(family, TfClassNames.Key("FamilyBase", "m_inPlace")) is not int inPlace) return null;
        if (inPlace == 0) return 1;
        return Field(family, TfClassNames.Key("FamilyBase", "m_isModel")) is int isModel && Field(family, TfClassNames.Key("FamilyBase", "m_is2d")) is int is2d && (isModel == 0 || is2d != 0) ? 0 : null;
    }

    // A family: an in-place family is; otherwise not a curtain panel, not in the curtain panel category and not hosted by type 1
    private static int? Standalone(Element family)
    {
        if (Field(family, TfClassNames.Key("FamilyBase", "m_inPlace")) is not int inPlace || Field(family, TfClassNames.Key("FamilyBase", "m_isCurtainPanel")) is not int curtain
            || Field(family, TfClassNames.Key("FamilyBase", "m_categoryId")) is not int category || Field(family, TfClassNames.Key("Family", "m_refTypeIds@0")) is not int host) return null;
        return inPlace != 0 ? 1 : (curtain != 0 || category == CurtainPanelsCategory || host == 1 ? 0 : 1);
    }

    // A family: a model family that is not in place, not 2d, standalone, not in the model groups category, and
    // either without dummy references or hosted by the generic host type (7)
    private static int? MayHaveFreeHost(Element family)
    {
        if (Standalone(family) is not int standalone || Field(family, TfClassNames.Key("FamilyBase", "m_inPlace")) is not int inPlace
            || Field(family, TfClassNames.Key("FamilyBase", "m_is2d")) is not int is2d || Field(family, TfClassNames.Key("FamilyBase", "m_isModel")) is not int isModel
            || Field(family, TfClassNames.Key("FamilyBase", "m_categoryId")) is not int category || Field(family, TfClassNames.Key("Family", "m_bHasAnyDummyRefs")) is not int dummy
            || Field(family, TfClassNames.Key("Family", "m_refTypeIds@0")) is not int host) return null;
        bool generic = host == 7;
        return Flag(inPlace == 0 && is2d == 0 && isModel != 0 && standalone != 0 && category != ModelGroupsCategory && (dummy == 0 || generic));
    }
}
