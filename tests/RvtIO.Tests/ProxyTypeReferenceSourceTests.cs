using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>Unit-level coverage of <see cref="ElementBuilder.ApplyValidatedProxyTypeReferenceNames"/>:
/// the multi-category extension of <see cref="ElementBuilder.ApplyValidatedWindowTypeReferenceNames"/>
/// to the IfcBuildingElementProxy population (generic model, specialty/mechanical/electrical
/// equipment, planting, mass, ...). Same mechanism, precedence and isolation from
/// <see cref="Element.Name"/>; the category gate is a set instead of a single constant, since
/// IfcExporter.MapCategoryToIfcType maps many BuiltInCategory ids to IFCBUILDINGELEMENTPROXY.
/// The result feeds Pset_BuildingElementProxyCommon.Reference.</summary>
public class ElementBuilderProxyTypeReferenceSourceTests
{
    private const int GenericModelCategoryId = -2000151;
    private const int MechanicalEquipmentCategoryId = -2000181;
    private const int DoorsCategoryId = -2000023;

    [Fact]
    public void PostProcess_StoresOwnMasterSymbolTableNameAsIsolatedReferenceSource()
    {
        var type = Proxy(200, 0, "Existing proxy type name", GenericModelCategoryId);
        var instance = Proxy(300, type.Id, "Existing proxy instance name", GenericModelCategoryId);
        var info = new RevitFileInfo();
        info.Elements.AddRange([type, instance]);
        var state = new ParsingState();
        state.NativeParams[(uint)instance.Id] = ProxyNativeParams(type.Id);
        state.ElementNameTable[(uint)type.Id] = "Canonical proxy Reference";

        ElementBuilder.PostProcess(info, state);

        Assert.Equal("Existing proxy type name", type.Name);
        // Only the Reference is set: it must not replace the main-product Name, which the
        // ordinary type-name propagation already resolved correctly here.
        Assert.Equal("Existing proxy type name", instance.Name);
        Assert.NotEqual("Canonical proxy Reference", instance.Name);
        Assert.Null(type.ValidatedProxyTypeReferenceName);
        Assert.Equal("Canonical proxy Reference", instance.ValidatedProxyTypeReferenceName);
    }

    [Theory]
    [InlineData("wrong-instance-category")]
    [InlineData("wrong-type-category")]
    [InlineData("mismatched-proxy-categories")]
    [InlineData("missing-native-row")]
    [InlineData("missing-native-field")]
    [InlineData("nonpositive-type")]
    [InlineData("self-type")]
    [InlineData("unresolved-type")]
    [InlineData("missing-table-name")]
    [InlineData("blank-table-name")]
    public void ProxyReferenceValidation_FailsClosedWithoutCompleteValidation(string brokenValidation)
    {
        var type = Proxy(200, 0, "Existing proxy type name", GenericModelCategoryId);
        var instance = Proxy(300, type.Id, "Existing proxy instance name", GenericModelCategoryId);
        instance.ValidatedProxyTypeReferenceName = "Stale value";
        var elements = new List<Element> { type, instance };
        var elementNameTable = new Dictionary<uint, string>
        {
            [(uint)type.Id] = "Unvalidated replacement",
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)instance.Id] = ProxyNativeParams(type.Id),
        };

        switch (brokenValidation)
        {
            case "wrong-instance-category":
                instance.CategoryId = DoorsCategoryId;
                break;
            case "wrong-type-category":
                type.CategoryId = DoorsCategoryId;
                break;
            case "mismatched-proxy-categories":
                // Both are valid IFCBUILDINGELEMENTPROXY categories, but not the SAME one:
                // Revit families are single-category, so instance/type disagreeing here means
                // the resolved id is not actually this instance's own FamilySymbol.
                type.CategoryId = MechanicalEquipmentCategoryId;
                break;
            case "missing-native-row":
                nativeParams.Clear();
                break;
            case "missing-native-field":
                nativeParams[(uint)instance.Id] = new();
                break;
            case "nonpositive-type":
                nativeParams[(uint)instance.Id] = ProxyNativeParams(0);
                break;
            case "self-type":
                nativeParams[(uint)instance.Id] = ProxyNativeParams(instance.Id);
                elementNameTable[(uint)instance.Id] = "Unvalidated replacement";
                break;
            case "unresolved-type":
                nativeParams[(uint)instance.Id] = ProxyNativeParams(999);
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

        ElementBuilder.ApplyValidatedProxyTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            nativeParams, elementNameTable);

        Assert.Equal("Existing proxy type name", type.Name);
        Assert.Equal("Existing proxy instance name", instance.Name);
        Assert.Null(instance.ValidatedProxyTypeReferenceName);
    }

    [Theory]
    [InlineData(GenericModelCategoryId)]
    [InlineData(MechanicalEquipmentCategoryId)]
    [InlineData(-2001350)] // OST_SpecialityEquipment
    [InlineData(-2003400)] // OST_Mass
    public void ProxyReferenceValidation_AppliesAcrossEveryProxyMappedCategory(int categoryId)
    {
        var type = Proxy(200, 0, "Existing proxy type name", categoryId);
        var instance = Proxy(300, type.Id, "Existing instance name", categoryId);
        var elements = new List<Element> { type, instance };
        var elementNameTable = new Dictionary<uint, string>
        {
            [(uint)type.Id] = "Canonical proxy Reference",
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)instance.Id] = ProxyNativeParams(type.Id),
        };

        ElementBuilder.ApplyValidatedProxyTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            nativeParams, elementNameTable);

        Assert.Equal("Canonical proxy Reference", instance.ValidatedProxyTypeReferenceName);
    }

    [Fact]
    public void ProxyReferenceValidation_UsesOwnMasterSymbolIdInsteadOfPreliminaryTypeId()
    {
        // An earlier same-category References/id heuristic can leave instance.TypeId pointing at the wrong
        // FamilySymbol. The rule must read the instance's own serialized m_masterSymbolId
        // directly, never the (possibly still-wrong at this point) structural TypeId field.
        var type = Proxy(200, 0, "Existing proxy type name", GenericModelCategoryId);
        var wrongPreliminaryType = Proxy(250, 0, "Wrong preliminary type", GenericModelCategoryId);
        var instance = Proxy(300, wrongPreliminaryType.Id, "Existing instance name", GenericModelCategoryId);
        var elements = new List<Element> { type, wrongPreliminaryType, instance };
        var elementNameTable = new Dictionary<uint, string>
        {
            [(uint)type.Id] = "Canonical proxy Reference",
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)instance.Id] = ProxyNativeParams(type.Id),
        };

        ElementBuilder.ApplyValidatedProxyTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            nativeParams, elementNameTable);

        Assert.Equal(wrongPreliminaryType.Id, instance.TypeId);
        Assert.Equal("Canonical proxy Reference", instance.ValidatedProxyTypeReferenceName);
        Assert.Equal("Existing instance name", instance.Name);
    }

    [Fact]
    public void SiblingWithoutOwnMasterSymbolId_NeverGetsValidatedReference()
    {
        var type = Proxy(200, 0, "Existing proxy type name", GenericModelCategoryId);
        var validatedInstance = Proxy(300, type.Id, "Existing validated instance name", GenericModelCategoryId);
        var unvalidatedSibling = Proxy(301, type.Id, "Existing sibling instance name", GenericModelCategoryId);
        var elements = new List<Element> { type, validatedInstance, unvalidatedSibling };
        var elementNameTable = new Dictionary<uint, string>
        {
            [(uint)type.Id] = "Canonical proxy Reference",
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)validatedInstance.Id] = ProxyNativeParams(type.Id),
            // unvalidatedSibling deliberately has no NativeParams row at all - the real-world
            // case of an instance whose own block did not decode.
        };

        ElementBuilder.ApplyValidatedProxyTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            nativeParams, elementNameTable);

        Assert.Equal("Canonical proxy Reference", validatedInstance.ValidatedProxyTypeReferenceName);
        Assert.Null(unvalidatedSibling.ValidatedProxyTypeReferenceName);
        Assert.Equal("Existing sibling instance name", unvalidatedSibling.Name);
    }

    [Fact]
    public void ProxyReferenceValidation_UsesExplicitInstanceReferenceBeforeTerminalName()
    {
        var type = Proxy(200, 0, "Existing proxy type name", GenericModelCategoryId);
        type.Parameters.Add(StringParameter("Reference", "Type Reference", 20));
        var instance = Proxy(300, 999, "Existing instance name", GenericModelCategoryId);
        instance.Parameters.Add(StringParameter("Reference", "Instance Reference", 10));
        var elements = new List<Element> { type, instance };

        ElementBuilder.ApplyValidatedProxyTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            new Dictionary<uint, TfBlockDeserializer.NativeParams>
            {
                [(uint)instance.Id] = ProxyNativeParams(type.Id),
            },
            new Dictionary<uint, string>());

        Assert.Equal("Instance Reference", instance.ValidatedProxyTypeReferenceName);
        Assert.True(instance.ValidatedProxyTypeReferenceIsExplicit);
        Assert.False(instance.ValidatedProxyTypeReferenceIsBlocked);
        Assert.Equal("Existing instance name", instance.Name);
    }

    [Fact]
    public void ProxyReferenceValidation_UnreproducibleExplicitParameterBlocksTerminalFallback()
    {
        var type = Proxy(200, 0, "Existing proxy type name", GenericModelCategoryId);
        var instance = Proxy(300, type.Id, "Existing instance name", GenericModelCategoryId);
        instance.Parameters.Add(new Parameter
        {
            Id = 10,
            Definition = new Definition { Id = 10, Name = "Reference" },
            StorageType = StorageType.Double,
            Value = 12.5,
        });
        var elements = new List<Element> { type, instance };

        ElementBuilder.ApplyValidatedProxyTypeReferenceNames(
            elements, elements.ToDictionary(element => element.Id),
            new Dictionary<uint, TfBlockDeserializer.NativeParams>
            {
                [(uint)instance.Id] = ProxyNativeParams(type.Id),
            },
            new Dictionary<uint, string>
            {
                [(uint)type.Id] = "Must not leak",
            });

        Assert.Null(instance.ValidatedProxyTypeReferenceName);
        Assert.False(instance.ValidatedProxyTypeReferenceIsExplicit);
        Assert.True(instance.ValidatedProxyTypeReferenceIsBlocked);
        Assert.Equal("Existing instance name", instance.Name);
    }

    private static TfBlockDeserializer.NativeParams ProxyNativeParams(int typeId)
    {
        var nativeParams = new TfBlockDeserializer.NativeParams();
        nativeParams.Fields["m_masterSymbolId"] = typeId;
        return nativeParams;
    }

    private static Element Proxy(int id, int typeId, string name, int categoryId) => new()
    {
        Id = id,
        TypeId = typeId,
        CategoryId = categoryId,
        Category = "OST_GenericModel",
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
