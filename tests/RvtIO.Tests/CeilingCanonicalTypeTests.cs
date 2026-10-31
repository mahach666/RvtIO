using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CeilingCanonicalTypeTests
{
    [Fact]
    public void OwnCompatibleRoots_ReplaceReferenceVotingTypeAndAllThreeTypeParameters()
    {
        var (elements, state) = Source();
        var occurrence = elements[0];
        occurrence.Name = "Existing occurrence";
        occurrence.FamilyName = "Existing family";
        occurrence.SerializedCeilingTypeId = 500;
        occurrence.Parameters.Add(Parameter(-1002050, "ELEM_TYPE_PARAM", 900));
        occurrence.Parameters.Add(Parameter(-1002052, "ELEM_FAMILY_AND_TYPE_PARAM", 901));
        occurrence.Parameters.Add(Parameter(-1002000, "SYMBOL_ID_PARAM", 902));
        occurrence.Parameters.Add(Parameter(700, "Authored", 903));
        var definitions = occurrence.Parameters.Select(parameter => parameter.Definition).ToArray();
        string typeBefore = JsonSerializer.Serialize(elements[1]);

        Apply(elements, state);

        Assert.Equal(500, occurrence.TypeId);
        Assert.Equal(500, occurrence.FamilyTypeId);
        Assert.Equal(new[] { 500, 500, 500, 903 }, occurrence.Parameters.Select(parameter => Assert.IsType<int>(parameter.Value)));
        Assert.All(occurrence.Parameters.Take(3), parameter => Assert.Equal(StorageType.ElementId, parameter.StorageType));
        Assert.Equal(definitions, occurrence.Parameters.Select(parameter => parameter.Definition));
        Assert.Equal("Existing occurrence", occurrence.Name);
        Assert.Equal("Existing family", occurrence.FamilyName);
        Assert.Equal(500, occurrence.SerializedCeilingTypeId);
        Assert.Equal(typeBefore, JsonSerializer.Serialize(elements[1]));
    }

    [Fact]
    public void MissingTypeParameters_AreCreatedOnce_AndRepeatedApplicationIsIdempotent()
    {
        var (elements, state) = Source();
        Apply(elements, state);
        Assert.Equal(500, elements[0].FamilyTypeId);
        Assert.Equal(3, elements[0].Parameters.Count);
        string before = JsonSerializer.Serialize(elements);
        Apply(elements, state);
        Assert.Equal(before, JsonSerializer.Serialize(elements));
        Assert.All(elements[0].Parameters, parameter =>
        {
            Assert.Equal(500, parameter.AsElementId());
            Assert.Equal(parameter.Id, parameter.Definition.Id);
        });
    }

    [Fact]
    public void DuplicateLegacyParameters_AreAllCanonicalized_NotUsedAsSourceAuthority()
    {
        var (elements, state) = Source();
        elements[0].Parameters.Add(Parameter(-1002050, "ELEM_TYPE_PARAM", 900));
        elements[0].Parameters.Add(Parameter(-1002050, "ELEM_TYPE_PARAM", 901));
        Apply(elements, state);
        Assert.Equal(2, elements[0].Parameters.Count(parameter => parameter.Id == -1002050));
        Assert.All(elements[0].Parameters, parameter => Assert.Equal(500, parameter.Value));
    }

    [Theory]
    [InlineData("missing_occurrence")] [InlineData("missing_type_root")]
    [InlineData("duplicate_occurrence_root")] [InlineData("duplicate_type_root")]
    [InlineData("duplicate_occurrence")] [InlineData("duplicate_type")]
    [InlineData("wrong_occurrence_class")] [InlineData("wrong_type_class")]
    [InlineData("wrong_occurrence_category")] [InlineData("wrong_type_category")]
    [InlineData("self_type")] [InlineData("unresolved_type")]
    [InlineData("block_only_occurrence")] [InlineData("block_only_type")]
    [InlineData("zero_type")] [InlineData("negative_type")]
    public void MissingOrConflictingValidation_DoesNotChangeLegacyPublicData(string mutation)
    {
        foreach (bool reverse in new[] { false, true })
        {
            var (elements, state) = Source();
            elements[0].Parameters.Add(Parameter(-1002050, "ELEM_TYPE_PARAM", 900));
            switch (mutation)
            {
                case "missing_occurrence": state.SystemOccurrenceTypeIds.Clear(); break;
                case "missing_type_root": state.SystemTypeRoots.Clear(); break;
                case "duplicate_occurrence_root": state.SystemOccurrenceTypeIds[100] = (2, 642, 500); break;
                case "duplicate_type_root": state.SystemTypeRoots[500] = (2, 646, null); break;
                case "duplicate_occurrence": elements.Add(new Element { Id = 100 }); break;
                case "duplicate_type": elements.Add(new Element { Id = 500 }); break;
                case "wrong_occurrence_class": state.SystemOccurrenceTypeIds[100] = (1, 1913, 500); break;
                case "wrong_type_class": state.SystemTypeRoots[500] = (1, 3607, null); break;
                case "wrong_occurrence_category": elements[0].CategoryId = -2000032; break;
                case "wrong_type_category": elements[1].CategoryId = -2000032; break;
                case "self_type": state.SystemOccurrenceTypeIds[100] = (1, 642, 100); break;
                case "unresolved_type": elements.RemoveAt(1); break;
                case "block_only_occurrence": elements[0].IsBlockOnly = true; break;
                case "block_only_type": elements[1].IsBlockOnly = true; break;
                case "zero_type": state.SystemOccurrenceTypeIds[100] = (1, 642, 0); break;
                case "negative_type": state.SystemOccurrenceTypeIds[100] = (1, 642, -1); break;
            }
            if (reverse) elements.Reverse();
            string before = JsonSerializer.Serialize(elements);
            Apply(elements, state);
            Assert.Equal(before, JsonSerializer.Serialize(elements));
        }
    }

    [Theory]
    [InlineData(3524, 512, -2000011)]
    [InlineData(1913, 1915, -2000032)]
    public void OtherValidatedSystemTypes_AreNotCanonicalizedByTheCeilingPass(int cls, int typeClass, int category)
    {
        var (elements, state) = Source();
        elements.ForEach(element => element.CategoryId = category);
        state.SystemOccurrenceTypeIds[100] = (1, (ushort)cls, 500);
        state.SystemTypeRoots[500] = (1, (ushort)typeClass, 0);
        string before = JsonSerializer.Serialize(elements);
        Apply(elements, state);
        Assert.Equal(500, elements[0].SerializedSystemTypeId);
        Assert.Equal(before, JsonSerializer.Serialize(elements));
    }

    [Theory]
    [InlineData("missing_marker")] [InlineData("wrong_marker")]
    [InlineData("missing_type_marker")] [InlineData("wrong_type_marker")]
    public void TheCanonicalizationPass_RequiresTheFreshCompatibleIdentityValidation(string mutation)
    {
        var (elements, state) = Source();
        ElementBuilder.ApplySerializedSystemTypeIdentity(elements, state);
        switch (mutation)
        {
            case "missing_marker": elements[0].SerializedSystemTypeId = null; break;
            case "wrong_marker": elements[0].SerializedSystemTypeId = 501; break;
            case "missing_type_marker": elements[1].SerializedSystemTypeClassId = null; break;
            case "wrong_type_marker": elements[1].SerializedSystemTypeClassId = 512; break;
        }
        string before = JsonSerializer.Serialize(elements);
        ElementBuilder.ApplyValidatedCeilingCanonicalTypes(elements, state);
        Assert.Equal(before, JsonSerializer.Serialize(elements));
    }

    private static void Apply(List<Element> elements, ParsingState state)
    {
        ElementBuilder.ApplySerializedSystemTypeIdentity(elements, state);
        ElementBuilder.ApplyValidatedCeilingCanonicalTypes(elements, state);
    }

    private static (List<Element>, ParsingState) Source()
    {
        var state = new ParsingState();
        state.SystemOccurrenceTypeIds[100] = (1, 642, 500);
        state.SystemTypeRoots[500] = (1, 646, null);
        return ([new Element { Id = 100, TypeId = 900, CategoryId = -2000038 },
            new Element { Id = 500, CategoryId = -2000038 }], state);
    }

    private static Parameter Parameter(int id, string name, int value) => new()
    {
        Id = id, StorageType = StorageType.ElementId, Value = value,
        Definition = new Definition { Id = id, Name = name },
    };
}
