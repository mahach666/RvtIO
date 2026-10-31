using RvtIO.Compact;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// The parameter-id lists of the classes learned from observed output (resource compact_observed_lists.tsv) and the value gate lines
/// that belong to them. The other tests state the rule of a missing decision.
/// </summary>
public class CompactObservedListsTests
{
    private static readonly CompactListFileFacts Plain = new(false, true, false);

    private static Element Make(int[]? stored = null, Dictionary<string, int>? fields = null) => new()
    {
        Id = 1,
        SerializedFamilyId = -1,
        SerializedUnplacedOwnerId = -1,
        SerializedOwnerDbViewId = -1,
        SerializedCreatedPhaseId = -1,
        NativeCellListTag = 0,
        NativeParameterStoreIds = [.. stored ?? []],
        NativeParameterStoresComplete = true,
        NativeListFields = fields,
    };

    private static CompactListResult Run(string recordClass, Element element, int header) =>
        CompactParameterLists.GetBuiltinIds(recordClass, new ElementListFacts(element, header, Plain));

    [Fact]
    public void IndependentTag_ListsTheLeaderAndTypeIdsWhateverTheCategory()
    {
        var result = Run("IndependentTag", Make(), -2005015);

        Assert.Null(result.Unknown);
        // Angle, Leader Line, Leader Type, Orientation and the four type entries
        Assert.Contains(-1007007, result.Ids!);
        Assert.Contains(-1006502, result.Ids!);
        Assert.Contains(-1002051, result.Ids!);
        Assert.Contains(-1002000, result.Ids!);
        Assert.Equal(result.Ids!.Order().ToArray(), result.Ids);
    }

    [Fact]
    public void RecordNotDecoded_LeavesTheListUnknown()
    {
        var element = Make();
        element.NativeCellListTag = null;

        var result = Run("CurveElem", element, -2000051);

        Assert.Null(result.Ids);
        Assert.Contains("not decoded", result.Unknown);
    }

    [Fact]
    public void IndependentTag_NeedsNoRecord_OnlyTheClass()
    {
        var element = Make();
        element.NativeCellListTag = null;

        Assert.Null(Run("IndependentTag", element, -2005015).Unknown);
    }

    [Fact]
    public void CurveElem_ModelLines_ListLineStyleAndDetailLine()
    {
        var result = Run("CurveElem", Make(), -2000051);

        Assert.Null(result.Unknown);
        Assert.Contains(-1006210, result.Ids!);
        Assert.Contains(-1004011, result.Ids!);
        Assert.DoesNotContain(-1006205, result.Ids!);
    }

    [Fact]
    public void CurveElem_StoredVisibility_IsListedWhenTheTableTestsIt()
    {
        var result = Run("CurveElem", Make(stored: [-1006205]), -2000051);

        Assert.Null(result.Unknown);
        Assert.Contains(-1006205, result.Ids!);
    }

    [Fact]
    public void CurveElem_CategoryNeverSeen_IsUnknown()
    {
        var result = Run("CurveElem", Make(), -2999999);

        Assert.Null(result.Ids);
        Assert.Contains("not observed", result.Unknown);
    }

    [Fact]
    public void ClassWithoutTable_IsNotCovered()
    {
        var result = Run("NoSuchClass", Make(), -1);

        Assert.Null(result.Ids);
        Assert.Contains("not covered", result.Unknown);
    }

    [Fact]
    public void ValueGate_OwnClass_AnswersOnlyFromItsOwnLines()
    {
        var gate = CompactValueGate.Parse("S\t-5\t10\nD\t-6\t10\nO\tA\nQ\tA\t-7\t3\nC\tA\t-8\t3\nC\tB\t-9\t3\n");

        Assert.Equal(CompactValueGate.Source.Stored, gate.Of("A", -7));
        Assert.Equal(CompactValueGate.Source.Derived, gate.Of("A", -8));
        // the global lines do not apply to a class with a table of its own
        Assert.Equal(CompactValueGate.Source.None, gate.Of("A", -5));
        Assert.Equal(CompactValueGate.Source.None, gate.Of("A", -6));
        // other classes are unchanged
        Assert.Equal(CompactValueGate.Source.Stored, gate.Of("B", -5));
        Assert.Equal(CompactValueGate.Source.Derived, gate.Of("B", -9));
        Assert.Equal(CompactValueGate.Source.None, gate.Of("B", -7));
    }
}
