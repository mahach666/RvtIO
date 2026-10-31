using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class StairComponentSourceTests
{
    [Fact]
    public void ElemRecOwnersAreIndependentOfHistoryAndNoPartialTableSurvives()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1312); writer.Write(2u);
        foreach (int id in new[] { 0, 200 })
        {
            writer.Write(id); writer.Write(id); writer.Write(-99); writer.Write(0);
            writer.Write(0); writer.Write(5); writer.Write(id == 0 ? -1 : 100);
        }
        byte[] bytes = stream.ToArray();
        Assert.NotNull(ElementExportIds.ParseElemTable(bytes, out var owners));
        Assert.Equal(-1, owners![0]); Assert.Equal(100, owners[200]);
        Assert.Null(ElementExportIds.ParseElemTable(bytes[..^1], out var partial)); Assert.Null(partial);
        BitConverter.GetBytes(0).CopyTo(bytes, 34);
        Assert.Null(ElementExportIds.ParseElemTable(bytes, out partial)); Assert.Null(partial);
    }

    [Fact]
    public void ReciprocalTypedClosureExcludesServiceSymbolsAndRegisteredRailings()
    {
        var (elements, state) = Fixture();
        StairComponentSource.Apply(elements, state);
        var core = Assert.Single(elements[0].SerializedStairCore!);
        Assert.Equal(new StairComponentSource.CoreComponent(200, 3708), core);
        Assert.Equal(100, elements[1].SerializedOwningElementId);
        Assert.Equal((ushort)3739, elements[2].SerializedOwnElementClassId);
        Assert.Null(elements[2].SerializedStairCore);
    }

    [Theory]
    [InlineData("no_owners")] [InlineData("wrong_owner")] [InlineData("parent_duplicate")]
    [InlineData("child_duplicate")] [InlineData("block_only_parent")] [InlineData("block_only_child")]
    [InlineData("parent_category")] [InlineData("parent_class")]
    [InlineData("duplicate_membership")] [InlineData("malformed_membership")]
    [InlineData("owner_mismatch")] [InlineData("duplicate_child_root")]
    [InlineData("missing_child_root")] [InlineData("missing_service_root")]
    [InlineData("ambiguous_service_root")] [InlineData("missing_materialized_child")]
    [InlineData("duplicate_member")] [InlineData("wrong_role")]
    [InlineData("unlisted_core")] [InlineData("empty_core")]
    [InlineData("missing_parent_record")]
    public void AmbiguousIncompleteOrConflictingAuthorityClearsTheWholeCore(string mode)
    {
        var (elements, state) = Fixture();
        StairComponentSource.Apply(elements, state); Assert.NotNull(elements[0].SerializedStairCore);
        switch (mode)
        {
            case "no_owners": state.ElemRecOwningElementIds = null; break;
            case "wrong_owner": state.ElemRecOwningElementIds![200] = 999; break;
            case "parent_duplicate": elements.Add(new Element { Id = 100 }); break;
            case "child_duplicate": elements.Add(new Element { Id = 200 }); break;
            case "block_only_parent": elements[0].IsBlockOnly = true; break;
            case "block_only_child": elements[1].IsBlockOnly = true; break;
            case "parent_category": elements[0].CategoryId = -2000919; break;
            case "parent_class": state.OwnedElementClassRoots[100] = new(1, 3742); break;
            case "duplicate_membership": state.StairMembershipRoots[100] = new(2, state.StairMembershipRoots[100].Value); break;
            case "malformed_membership": state.StairMembershipRoots[100] = new(1, null); break;
            case "owner_mismatch": state.StairMembershipRoots[100] = new(1, Membership(999)); break;
            case "duplicate_child_root": state.OwnedElementClassRoots[200] = new(2, 3708); break;
            case "missing_child_root": state.OwnedElementClassRoots.Remove(200); break;
            case "missing_service_root": state.OwnedElementClassRoots.Remove(300); break;
            case "ambiguous_service_root": state.OwnedElementClassRoots[300] = new(2, null); break;
            case "missing_materialized_child": elements.RemoveAt(1); break;
            case "duplicate_member": state.StairMembershipRoots[100] = new(1, Membership(100, [200, 200])); break;
            case "wrong_role": state.StairMembershipRoots[100] = new(1, new(100, [], [200], [], -1)); break;
            case "unlisted_core": state.OwnedElementClassRoots[300] = new(1, 3686); break;
            case "empty_core": state.StairMembershipRoots[100] = new(1, Membership(100, [])); break;
            case "missing_parent_record": state.ElemRecOwningElementIds!.Remove(100); break;
        }
        StairComponentSource.Apply(elements, state);
        Assert.Null(elements[0].SerializedStairCore);
    }

    [Fact]
    public void MissingServiceMaterializationDoesNotHideItsValidatedNonCoreClass()
    {
        var (elements, state) = Fixture(); elements.RemoveAt(2);
        StairComponentSource.Apply(elements, state); Assert.Single(elements[0].SerializedStairCore!);
    }

    [Theory]
    [InlineData(3708, false)] [InlineData(3686, false)] [InlineData(3727, true)]
    public void NativeRolesAreRetainedWithoutNamingOrdinals(ushort cls, bool support)
    {
        var (elements, state) = Fixture();
        state.OwnedElementClassRoots[200] = new(1, cls);
        // Own native classes, not the legacy normalized category, define the roles.
        elements[1].CategoryId = -2000151;
        state.StairMembershipRoots[100] = new(1, new(100, support ? [] : [200], support ? [200] : [], [], -1));
        StairComponentSource.Apply(elements, state);
        Assert.Equal(new StairComponentSource.CoreComponent(200, cls), Assert.Single(elements[0].SerializedStairCore!));
    }

    [Fact]
    public void EveryParentRootIsCountedIncludingMalformedDuplicates()
    {
        byte[] block = Convert.FromBase64String(StairElementSerializationTests.ExactStairBlockBase64);
        uint size = BitConverter.ToUInt32(block, 8); var state = new ParsingState();
        StairComponentSource.Record(state, block, 0, size, 3681, 513254);
        Assert.NotNull(state.StairMembershipRoots[513254].Value);
        Assert.Equal((ushort)3681, state.OwnedElementClassRoots[513254].ClassId);
        block[^1] ^= 1;
        StairComponentSource.Record(state, block, 0, size, 3681, 513254);
        Assert.Equal(2, state.StairMembershipRoots[513254].BlockCount);
        Assert.Null(state.StairMembershipRoots[513254].Value);
        Assert.Equal(2, state.OwnedElementClassRoots[513254].BlockCount);
        Assert.Null(state.OwnedElementClassRoots[513254].ClassId);
    }

    [Fact]
    public void CompleteRunInputNeedsReciprocalElemRecAndIsNotPublicJson()
    {
        var (elements, state) = Fixture();
        var input = new TfBlockDeserializer.StairRunInput(200, 100, TfBlockDeserializer.StairRunStyle.Straight, [new(1, 0, 0)]);
        state.StairRunInputRoots[200] = (1, input);
        StairComponentSource.Apply(elements, state);
        Assert.Same(input, elements[1].SerializedStairRunInput);
        Assert.DoesNotContain("SerializedStairRunInput", System.Text.Json.JsonSerializer.Serialize(elements[1]));
    }

    [Theory]
    [InlineData("no_table")] [InlineData("owner")] [InlineData("root_duplicate")]
    [InlineData("input_duplicate")] [InlineData("input_null")] [InlineData("input_owner")]
    [InlineData("input_parent")] [InlineData("class")] [InlineData("block")]
    [InlineData("element_duplicate")] [InlineData("input_missing")]
    public void RunInputsAreClearedOnAmbiguousOrMissingIndependentSource(string mode)
    {
        var (elements, state) = Fixture();
        var input = new TfBlockDeserializer.StairRunInput(200, 100, TfBlockDeserializer.StairRunStyle.Straight, [new(1, 0, 0)]);
        state.StairRunInputRoots[200] = (1, input);
        StairComponentSource.Apply(elements, state); Assert.NotNull(elements[1].SerializedStairRunInput);
        switch (mode)
        {
            case "no_table": state.ElemRecOwningElementIds = null; break;
            case "owner": state.ElemRecOwningElementIds![200] = 999; break;
            case "root_duplicate": state.OwnedElementClassRoots[200] = new(2, 3708); break;
            case "input_duplicate": state.StairRunInputRoots[200] = (2, input); break;
            case "input_null": state.StairRunInputRoots[200] = (1, null); break;
            case "input_owner": state.StairRunInputRoots[200] = (1, input with { OwnerId = 999 }); break;
            case "input_parent": state.StairRunInputRoots[200] = (1, input with { StairsId = 999 }); break;
            case "class": state.OwnedElementClassRoots[200] = new(1, 3686); break;
            case "block": elements[1].IsBlockOnly = true; break;
            case "element_duplicate": elements.Add(new() { Id = 200 }); break;
            case "input_missing": state.StairRunInputRoots.Remove(200); break;
        }
        StairComponentSource.Apply(elements, state); Assert.Null(elements[1].SerializedStairRunInput);
    }

    [Fact]
    public void MalformedDuplicateRunInputCannotBeHiddenByScanOrder()
    {
        byte[] block = StairRunInputTests.Build().Bytes;
        uint size = BitConverter.ToUInt32(block, 8);
        var state = new ParsingState { ElemRecOwningElementIds = new() { [42] = 43 } };
        StairComponentSource.Record(state, block, 0, size, 3708, 42);
        Assert.NotNull(state.StairRunInputRoots[42].Input);
        block[^1] ^= 1;
        StairComponentSource.Record(state, block, 0, size, 3708, 42);
        Assert.Equal(2, state.StairRunInputRoots[42].BlockCount); Assert.Null(state.StairRunInputRoots[42].Input);
        var reversed = new ParsingState { ElemRecOwningElementIds = new() { [42] = 43 } };
        StairComponentSource.Record(reversed, block, 0, size, 3708, 42);
        block[^1] ^= 1;
        StairComponentSource.Record(reversed, block, 0, size, 3708, 42);
        Assert.Equal(2, reversed.StairRunInputRoots[42].BlockCount); Assert.Null(reversed.StairRunInputRoots[42].Input);
    }

    private static TfBlockDeserializer.StairComponentIds Membership(int owner, int[]? core = null)
        => new(owner, core ?? [200], [], [400], -1);
    private static (List<Element>, ParsingState) Fixture()
    {
        var state = new ParsingState { ElemRecOwningElementIds = new() { [100] = -1, [200] = 100, [300] = 100, [400] = -1 } };
        state.StairMembershipRoots[100] = new(1, Membership(100));
        state.OwnedElementClassRoots[100] = new(1, 3681);
        state.OwnedElementClassRoots[200] = new(1, 3708);
        state.OwnedElementClassRoots[300] = new(1, 3739);
        return ([new() { Id = 100, CategoryId = -2000120 }, new() { Id = 200, CategoryId = -2000919 },
            new() { Id = 300, CategoryId = -2000921 }, new() { Id = 400, CategoryId = -2000126 }], state);
    }
}
