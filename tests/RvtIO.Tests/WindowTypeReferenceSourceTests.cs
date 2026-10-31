using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Unit-level coverage of <see cref="ElementBuilder.ApplyValidatedWindowTypeReferenceNames"/>,
/// the family-symbol analogue of the host-object wall/floor/ceiling native-provenance
/// rules. A window's type is a FamilySymbol, not a same-category typed value block, so the per-type
/// record is the instance's own directly decoded FamilyInstance.m_masterSymbolId
/// schema field resolved through ParsingState.ElementNameTable, not a 0xFFFFxxxx block.</summary>
public class ElementBuilderWindowTypeReferenceSourceTests
{
    private const int WindowsCategoryId = -2000014;

    [Fact]
    public void PostProcess_StoresOwnMasterSymbolTableNameAsIsolatedReferenceSource()
    {
        var type = Window(200, 0, "Existing window type name");
        var instance = Window(300, type.Id, "Existing window instance name");
        var info = new RevitFileInfo();
        info.Elements.AddRange([type, instance]);
        var state = new ParsingState();
        state.NativeParams[(uint)instance.Id] = WindowNativeParams(type.Id);
        state.ElementNameTable[(uint)type.Id] = "Canonical window Reference";

        ElementBuilder.PostProcess(info, state);

        Assert.Equal("Existing window type name", type.Name);
        // The ordinary type-name propagation predates this rule (it runs before
        // ApplyNativeParams/this rule) and this instance's TypeId already resolved
        // correctly from the start, so it inherits the type's Name as normal. The
        // name-table Reference value must not replace that baseline main-product name.
        Assert.Equal("Existing window type name", instance.Name);
        Assert.NotEqual("Canonical window Reference", instance.Name);
        Assert.Null(type.ValidatedWindowTypeReferenceName);
        Assert.Equal("Canonical window Reference", instance.ValidatedWindowTypeReferenceName);
    }

    [Theory]
    [InlineData("wrong-instance-category")]
    [InlineData("wrong-type-category")]
    [InlineData("missing-native-row")]
    [InlineData("missing-native-field")]
    [InlineData("nonpositive-type")]
    [InlineData("self-type")]
    [InlineData("unresolved-type")]
    [InlineData("missing-table-name")]
    [InlineData("blank-table-name")]
    public void WindowReferenceValidation_FailsClosedWithoutCompleteValidation(string brokenValidation)
    {
        var type = Window(200, 0, "Existing window type name");
        var instance = Window(300, type.Id, "Existing window instance name");
        instance.ValidatedWindowTypeReferenceName = "Stale value";
        var elements = new List<Element> { type, instance };
        var elementNameTable = new Dictionary<uint, string>
        {
            [(uint)type.Id] = "Unvalidated replacement",
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)instance.Id] = WindowNativeParams(type.Id),
        };

        switch (brokenValidation)
        {
            case "wrong-instance-category":
                instance.CategoryId = -2000023; // OST_Doors
                break;
            case "wrong-type-category":
                type.CategoryId = -2000023;
                break;
            case "missing-native-row":
                nativeParams.Clear();
                break;
            case "missing-native-field":
                nativeParams[(uint)instance.Id] = new();
                break;
            case "nonpositive-type":
                nativeParams[(uint)instance.Id] = WindowNativeParams(0);
                break;
            case "self-type":
                nativeParams[(uint)instance.Id] = WindowNativeParams(instance.Id);
                elementNameTable[(uint)instance.Id] = "Unvalidated replacement";
                break;
            case "unresolved-type":
                nativeParams[(uint)instance.Id] = WindowNativeParams(999);
                break;
            case "missing-table-name":
                elementNameTable.Clear();
                break;
            case "blank-table-name":
                elementNameTable[(uint)type.Id] = "   ";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(brokenValidation));
        }

        ElementBuilder.ApplyValidatedWindowTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            nativeParams, elementNameTable);

        Assert.Equal("Existing window type name", type.Name);
        Assert.Equal("Existing window instance name", instance.Name);
        Assert.Null(instance.ValidatedWindowTypeReferenceName);
    }

    [Fact]
    public void WindowReferenceValidation_UsesOwnMasterSymbolIdInsteadOfPreliminaryTypeId()
    {
        // An earlier same-category id heuristic can leave instance.TypeId pointing at the wrong
        // FamilySymbol, so the rule must read the instance's own serialized m_masterSymbolId
        // directly, never the structural TypeId field.
        var type = Window(200, 0, "Existing window type name");
        var wrongPreliminaryType = Window(250, 0, "Wrong preliminary type");
        var instance = Window(300, wrongPreliminaryType.Id, "Existing instance name");
        var elements = new List<Element> { type, wrongPreliminaryType, instance };
        var elementNameTable = new Dictionary<uint, string>
        {
            [(uint)type.Id] = "Canonical window Reference",
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)instance.Id] = WindowNativeParams(type.Id),
        };

        ElementBuilder.ApplyValidatedWindowTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            nativeParams, elementNameTable);

        Assert.Equal(wrongPreliminaryType.Id, instance.TypeId);
        Assert.Equal(
            "Canonical window Reference", instance.ValidatedWindowTypeReferenceName);
        Assert.Equal("Existing instance name", instance.Name);
    }

    [Fact]
    public void SiblingWithoutOwnMasterSymbolId_NeverGetsValidatedReference()
    {
        // A validated Reference must never leak to a sibling
        // instance that lacks its own native provenance link, even when it shares the same
        // resolved TypeId as a validated instance of the same type.
        var type = Window(200, 0, "Existing window type name");
        var validatedInstance = Window(300, type.Id, "Existing validated instance name");
        var unvalidatedSibling = Window(301, type.Id, "Existing sibling instance name");
        var elements = new List<Element> { type, validatedInstance, unvalidatedSibling };
        var elementNameTable = new Dictionary<uint, string>
        {
            [(uint)type.Id] = "Canonical window Reference",
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)validatedInstance.Id] = WindowNativeParams(type.Id),
            // unvalidatedSibling deliberately has no NativeParams row at all - the
            // real-world case of an instance whose own block did not decode.
        };

        ElementBuilder.ApplyValidatedWindowTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            nativeParams, elementNameTable);

        Assert.Equal(
            "Canonical window Reference", validatedInstance.ValidatedWindowTypeReferenceName);
        Assert.Null(unvalidatedSibling.ValidatedWindowTypeReferenceName);
        Assert.Equal("Existing sibling instance name", unvalidatedSibling.Name);
    }

    [Fact]
    public void WindowReferenceValidation_UsesExplicitInstanceThenExactRawTypeBeforeTerminalName()
    {
        var type = Window(200, 0, "Existing window type name");
        type.Parameters.Add(StringParameter("Reference", "Type Reference", 20));
        var instance = Window(300, 999, "Existing instance name");
        instance.Parameters.Add(StringParameter("Reference", "Instance Reference", 10));
        var elements = new List<Element> { type, instance };

        ElementBuilder.ApplyValidatedWindowTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            new Dictionary<uint, TfBlockDeserializer.NativeParams>
            {
                [(uint)instance.Id] = WindowNativeParams(type.Id),
            },
            new Dictionary<uint, string>());

        Assert.Equal("Instance Reference", instance.ValidatedWindowTypeReferenceName);
        Assert.True(instance.ValidatedWindowTypeReferenceIsExplicit);
        Assert.False(instance.ValidatedWindowTypeReferenceIsBlocked);
        Assert.Equal("Existing instance name", instance.Name);
    }

    [Fact]
    public void WindowReferenceValidation_EmptyInstanceFallsThroughToExactRawTypeSuffix()
    {
        var type = Window(200, 0, "Existing window type name");
        type.Parameters.Add(StringParameter("Reference[Type]", "Type suffix", 20));
        var instance = Window(300, 999, "Existing instance name");
        instance.Parameters.Add(StringParameter("Reference", "", 10));
        var elements = new List<Element> { type, instance };

        ElementBuilder.ApplyValidatedWindowTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            new Dictionary<uint, TfBlockDeserializer.NativeParams>
            {
                [(uint)instance.Id] = WindowNativeParams(type.Id),
            },
            new Dictionary<uint, string>
            {
                [(uint)type.Id] = "Terminal type name",
            });

        Assert.Equal("Type suffix", instance.ValidatedWindowTypeReferenceName);
        Assert.True(instance.ValidatedWindowTypeReferenceIsExplicit);
        Assert.False(instance.ValidatedWindowTypeReferenceIsBlocked);
    }

    [Fact]
    public void WindowReferenceValidation_UnreproducibleExplicitParameterBlocksTerminalFallback()
    {
        var type = Window(200, 0, "Existing window type name");
        var instance = Window(300, type.Id, "Existing instance name");
        instance.Parameters.Add(new Parameter
        {
            Id = 10,
            Definition = new Definition { Id = 10, Name = "Reference" },
            StorageType = StorageType.Double,
            Value = 12.5,
        });
        var elements = new List<Element> { type, instance };

        ElementBuilder.ApplyValidatedWindowTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            new Dictionary<uint, TfBlockDeserializer.NativeParams>
            {
                [(uint)instance.Id] = WindowNativeParams(type.Id),
            },
            new Dictionary<uint, string>
            {
                [(uint)type.Id] = "Must not leak",
            });

        Assert.Null(instance.ValidatedWindowTypeReferenceName);
        Assert.False(instance.ValidatedWindowTypeReferenceIsExplicit);
        Assert.True(instance.ValidatedWindowTypeReferenceIsBlocked);
        Assert.Equal("Existing instance name", instance.Name);
    }

    private static TfBlockDeserializer.NativeParams WindowNativeParams(int typeId)
    {
        var nativeParams = new TfBlockDeserializer.NativeParams();
        nativeParams.Fields["m_masterSymbolId"] = typeId;
        return nativeParams;
    }

    private static Element Window(int id, int typeId, string name) => new()
    {
        Id = id,
        TypeId = typeId,
        CategoryId = WindowsCategoryId,
        Category = "OST_Windows",
        Name = name,
    };

    private static Parameter StringParameter(
        string name, string value, int id) => new()
    {
        Id = id,
        Definition = new Definition { Id = id, Name = name },
        StorageType = StorageType.String,
        Value = value,
    };
}
