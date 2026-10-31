using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// Unit coverage for <see cref="ElementBuilder.ApplyValidatedFamilyNamesFromCanonicalTypeId"/>.
/// It replays the name-table chain (symbol -> NameEntryParentHolder -> NameHolderTarget ->
/// ElementNameTable) of the general FamilyName pass, keyed on the canonical, post-ApplyNativeParams
/// <see cref="Element.TypeId"/>. It applies to every category and fails closed when any link of the
/// chain is missing, leaving the FamilyName the general pass computed untouched.
/// </summary>
public class ValidatedFamilyNameFromCanonicalTypeIdTests
{
    private const int SymbolId = 500;
    private const int HolderId = 600;
    private const int FamilyId = 700;
    private const string FamilyNameValue = "Family from table";

    [Fact]
    public void ResolvesFamilyNameFromCanonicalTypeIdChain()
    {
        var instance = Instance(typeId: SymbolId, existingFamilyName: null);
        var state = ValidatedState();

        Apply([instance], state);

        Assert.Equal(FamilyNameValue, instance.FamilyName);
    }

    [Fact]
    public void OverridesAnExistingWrongFamilyNameGuess()
    {
        // Simulates the general pass's OST_* fallback / a wrong pre-ApplyNativeParams guess.
        var instance = Instance(typeId: SymbolId, existingFamilyName: "OST_Walls");
        var state = ValidatedState();

        Apply([instance], state);

        Assert.Equal(FamilyNameValue, instance.FamilyName);
    }

    [Fact]
    public void IsIdempotentWhenRunTwice()
    {
        var instance = Instance(typeId: SymbolId, existingFamilyName: null);
        var state = ValidatedState();

        Apply([instance], state);
        Apply([instance], state);

        Assert.Equal(FamilyNameValue, instance.FamilyName);
    }

    [Fact]
    public void EachElementIsResolvedIndependently_SiblingWithoutOwnChainStaysUntouched()
    {
        var validated = Instance(typeId: SymbolId, existingFamilyName: null, id: 300);
        // Sibling shares nothing with the validated symbol's chain - its own TypeId never
        // resolves, so its pre-existing FamilyName must not be overwritten or borrowed from
        // the validated instance (no cross-element leakage).
        var sibling = Instance(typeId: 999, existingFamilyName: "OST_Doors", id: 301);
        var state = ValidatedState();

        Apply([validated, sibling], state);

        Assert.Equal(FamilyNameValue, validated.FamilyName);
        Assert.Equal("OST_Doors", sibling.FamilyName);
    }

    [Theory]
    [InlineData("nonpositive-type-id")]
    [InlineData("no-parent-holder")]
    [InlineData("no-holder-target")]
    [InlineData("no-table-name")]
    [InlineData("blank-table-name")]
    public void FailsClosedWithoutEveryProvenanceLink(string brokenLink)
    {
        var instance = Instance(typeId: SymbolId, existingFamilyName: "Existing family name");
        var state = ValidatedState();

        switch (brokenLink)
        {
            case "nonpositive-type-id":
                instance.TypeId = 0;
                break;
            case "no-parent-holder":
                state.NameEntryParentHolder.Clear();
                break;
            case "no-holder-target":
                state.NameHolderTarget.Clear();
                break;
            case "no-table-name":
                state.ElementNameTable.Clear();
                break;
            case "blank-table-name":
                state.ElementNameTable[FamilyId] = "";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(brokenLink));
        }

        Apply([instance], state);

        Assert.Equal("Existing family name", instance.FamilyName);
    }

    [Fact]
    public void SystemFamilyTypeIdWithNoNameTableEntry_LeavesFamilyNameUntouched()
    {
        // Wall/Floor/Ceiling/Roof-shaped case: TypeId resolves to a real, positive id (the
        // wall type), but that id was never a NameTableScanner target (system families are not
        // FamilySymbol name-holder records) - the chain must fail closed, not synthesize or
        // guess a value.
        var instance = Instance(typeId: 16121, existingFamilyName: null);
        var state = ValidatedState();
        // Deliberately no NameEntryParentHolder[16121] entry.

        Apply([instance], state);

        Assert.Null(instance.FamilyName);
    }

    [Fact]
    public void PostProcess_ResolvesFamilyNameOnlyAfterNativeTypeIdCanonicalization()
    {
        // Call-site regression: the general (pre-ApplyNativeParams) fan-out leaves TypeId pointing at a wrong
        // but same-category decoy; only the m_masterSymbolId decode of ApplyNativeParams corrects it. The name-table
        // chain exists only for the true symbol, so this fails if the call site moves before ApplyNativeParams in PostProcess.
        const int categoryId = -2000171; // OST_CurtainWallMullions
        var decoyType = new Element
        {
            Id = 850, TypeId = -1, CategoryId = categoryId, Category = "OST_CurtainWallMullions",
            Name = "Decoy type",
        };
        var trueSymbol = new Element
        {
            Id = SymbolId, TypeId = -1, CategoryId = categoryId, Category = "OST_CurtainWallMullions",
            Name = "True type",
        };
        var instance = new Element
        {
            Id = 300, TypeId = decoyType.Id, CategoryId = categoryId, Category = "OST_CurtainWallMullions",
        };
        var info = new RevitFileInfo();
        info.Elements.AddRange([decoyType, trueSymbol, instance]);
        var state = ValidatedState();
        state.NativeParams[(uint)instance.Id] = MasterNativeParams(SymbolId);

        ElementBuilder.PostProcess(info, state);

        Assert.Equal(SymbolId, instance.TypeId);
        Assert.Equal(FamilyNameValue, instance.FamilyName);
    }

    private static TfBlockDeserializer.NativeParams MasterNativeParams(int masterId)
    {
        var nativeParams = new TfBlockDeserializer.NativeParams();
        nativeParams.Fields["m_masterSymbolId"] = masterId;
        return nativeParams;
    }

    private static void Apply(IReadOnlyList<Element> elements, ParsingState state) =>
        ElementBuilder.ApplyValidatedFamilyNamesFromCanonicalTypeId(elements, state);

    private static ParsingState ValidatedState()
    {
        var state = new ParsingState();
        state.NameEntryParentHolder[SymbolId] = HolderId;
        state.NameHolderTarget[HolderId] = FamilyId;
        state.ElementNameTable[FamilyId] = FamilyNameValue;
        return state;
    }

    private static Element Instance(int typeId, string? existingFamilyName, int id = 300) =>
        new()
        {
            Id = id,
            TypeId = typeId,
            FamilyName = existingFamilyName,
        };
}
