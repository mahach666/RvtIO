using System.Reflection;
using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class IfcSystemTypeOwnNameTests
{
    [Theory]
    [InlineData(1915, -2000032, "IFCSLAB")]
    [InlineData(1915, -2001300, "IFCSLAB")]
    [InlineData(2559, -2000011, "IFCCURTAINWALL")]
    public void TypeWriter_UsesOwnSymbolInfoWithOriginalResourceFamily_WithoutPublicMutation(
        int cls, int category, string product)
    {
        var (elements, state) = Scene(cls, category);
        string before = JsonSerializer.Serialize(elements);
        Apply(elements, state, cls);
        Assert.Equal(before, JsonSerializer.Serialize(elements));
        Assert.Contains("'Native family:Own type'", TypeArgs(Write(elements, product)));
        Assert.Equal(before, JsonSerializer.Serialize(elements));
    }

    [Theory]
    [InlineData("missing_name")] [InlineData("blank_name")]
    [InlineData("missing_family")] [InlineData("missing_host")]
    [InlineData("duplicate_host")] [InlineData("duplicate_root")]
    [InlineData("duplicate_type")] [InlineData("block_only")]
    [InlineData("wrong_host_class")] [InlineData("wrong_root_class")]
    [InlineData("wrong_discriminator")] [InlineData("missing_type_marker")]
    public void Floor_MissingOrConflictingValidation_RetainsLegacyName(string mutation)
    {
        var (elements, state) = Scene(1915, -2000032);
        var type = elements[1];
        switch (mutation)
        {
            case "missing_name": state.HostTypeCompoundStructures[500] = (1, 1915, [], null); break;
            case "blank_name": state.HostTypeCompoundStructures[500] = (1, 1915, [], " \t"); break;
            case "missing_family": type.ValidatedSystemFamilyName = null; break;
            case "missing_host": state.HostTypeCompoundStructures.Clear(); break;
            case "duplicate_host": state.HostTypeCompoundStructures[500] = (2, 1915, null, null); break;
            case "duplicate_root": state.SystemTypeRoots[500] = (2, 1915, null); break;
            case "duplicate_type": elements.Add(new Element { Id = 500 }); break;
            case "block_only": type.IsBlockOnly = true; break;
            case "wrong_host_class": state.HostTypeCompoundStructures[500] = (1, 646, [], "Wrong"); break;
            case "wrong_root_class": state.SystemTypeRoots[500] = (1, 646, null); break;
            case "wrong_discriminator": state.SystemTypeRoots[500] = (1, 1915, 1); break;
            case "missing_type_marker": break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Apply(elements, state, 1915);
        if (mutation == "missing_type_marker") type.SerializedSystemTypeClassId = null;
        Assert.Contains("'Legacy family:Legacy type'", TypeArgs(Write(elements, "IFCSLAB")));
    }

    [Theory]
    [InlineData("missing_name")] [InlineData("blank_name")]
    [InlineData("missing_family")] [InlineData("duplicate_root")]
    [InlineData("duplicate_type")] [InlineData("block_only")]
    [InlineData("missing_function")] [InlineData("invalid_function")]
    [InlineData("basic_function_conflict")] [InlineData("wrong_type_marker")]
    public void Curtain_MissingOrConflictingValidation_RetainsLegacyName(string mutation)
    {
        var (elements, state) = Scene(2559, -2000011);
        var type = elements[1];
        switch (mutation)
        {
            case "missing_name": state.NewCurtainWallTypeNames.Clear(); break;
            case "blank_name": state.NewCurtainWallTypeNames[500] = " \t"; break;
            case "missing_family": type.ValidatedSystemFamilyName = null; break;
            case "duplicate_root": state.SystemTypeRoots[500] = (2, 2559, null);
                state.NewCurtainWallTypeFunctions[500] = (2, null); break;
            case "duplicate_type": elements.Add(new Element { Id = 500 }); break;
            case "block_only": type.IsBlockOnly = true; break;
            case "missing_function": type.SerializedCurtainWallFunction = null; break;
            case "invalid_function": type.SerializedCurtainWallFunction = 6; break;
            case "basic_function_conflict": type.SerializedBasicWallTypeFunction = 1; break;
            case "wrong_type_marker": break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Apply(elements, state, 2559);
        if (mutation == "wrong_type_marker") type.SerializedSystemTypeClassId = 512;
        Assert.Contains("'Legacy family:Legacy type'", TypeArgs(Write(elements, "IFCCURTAINWALL")));
    }

    [Theory]
    [InlineData(1915, -2000032, "IFCSLAB")]
    [InlineData(2559, -2000011, "IFCCURTAINWALL")]
    public void ExistingOwnTableName_RetainsPriority(int cls, int category, string product)
    {
        var (elements, state) = Scene(cls, category);
        Apply(elements, state, cls);
        elements[1].ValidatedIfcTypeObjectName = "Own table family:Own table type";
        Assert.Contains("'Own table family:Own table type'", TypeArgs(Write(elements, product)));
    }

    [Theory]
    [InlineData(1915, -2000032, "IFCSLAB")]
    [InlineData(2559, -2000011, "IFCCURTAINWALL")]
    public void OnlyTypeNameChanges_AndClearingValidationRestoresLegacy(int cls, int category, string product)
    {
        var (elements, state) = Scene(cls, category);
        var before = Write(elements, product);
        Apply(elements, state, cls);
        var after = Write(elements, product);
        Assert.Equal(before.Select(row => (row.Id, row.Type)), after.Select(row => (row.Id, row.Type)));
        for (int index = 0; index < before.Length; index++)
            Assert.Equal(before[index].Type.EndsWith("TYPE", StringComparison.Ordinal)
                ? before[index].Args.Replace("'Legacy family:Legacy type'", "'Native family:Own type'")
                : before[index].Args, after[index].Args);
        Apply(elements, new ParsingState(), cls);
        Assert.Equal(before, Write(elements, product));
    }

    private static (List<Element>, ParsingState) Scene(int cls, int category)
    {
        var elements = new List<Element>
        {
            new() { Id = 100, TypeId = 500, CategoryId = category,
                Parameters = [new Parameter { Id = -1002050, Value = 500, StorageType = StorageType.ElementId,
                    Definition = new Definition { Id = -1002050, Name = "ELEM_TYPE_PARAM" } }] },
            new() { Id = 500, TypeId = -1, CategoryId = category, Name = "Legacy type",
                FamilyName = "Legacy family", ValidatedSystemFamilyName = "Native family",
                SerializedCurtainWallFunction = cls == 2559 ? 1 : null },
        };
        var state = new ParsingState();
        state.SystemOccurrenceTypeIds[100] = (1, (ushort)(cls == 2559 ? 3524 : 1913), 500);
        state.SystemTypeRoots[500] = (1, (ushort)cls, cls == 1915 ? category == -2001300 ? 1 : 0 : null);
        state.HostTypeCompoundStructures[500] = (1, (ushort)cls, [], "Own type");
        state.NewCurtainWallTypeFunctions[500] = (1, 1);
        state.NewCurtainWallTypeNames[500] = "Own type";
        return (elements, state);
    }

    private static void Apply(List<Element> elements, ParsingState state, int cls)
    {
        ElementBuilder.ApplySerializedSystemTypeIdentity(elements, state);
        if (cls == 2559) ElementBuilder.ApplyCurtainTypeNames(elements, state);
        else ElementBuilder.ApplyHostTypeCompoundStructures(elements, state);
    }

    private static (int Id, string Type, string Args)[] Write(List<Element> elements, string product)
    {
        var writer = new StepWriter();
        // Same real type writer used by Export; no duplicated naming implementation.
        typeof(IfcExporter).GetMethod("WriteTypeObjectsAndRelDefinesByType", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [writer, 900, elements.GroupBy(element => element.Id).ToDictionary(group => group.Key, group => group.First()),
                new Dictionary<int, (int, int, string)> { [100] = (1100, 0, product) },
                new Dictionary<(string, string), int>(), new Dictionary<int, bool?>()]);
        return Enumerable.Range(0, writer.EntityCount).Select(writer.EntityAt).ToArray();
    }

    private static string TypeArgs((int Id, string Type, string Args)[] rows) =>
        Assert.Single(rows, row => row.Type.EndsWith("TYPE", StringComparison.Ordinal)
            && row.Type != "IFCRELDEFINESBYTYPE").Args;
}
