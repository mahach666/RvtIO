using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class ElemScanStringsMergeTests
{
    private const uint ElementId = 42;
    private const string A = "A candidate";
    private const string B = "B candidate";
    private const string C = "C candidate";
    private const string Same = "same";

    [Fact]
    public void ReverseCompletionOrder_PreservesSourceChunkOrderAndDeduplicates()
    {
        var partialResults = ReverseCompletedResults();
        var merged = new Dictionary<uint, List<string>>();

        RvtFileReader.MergeElemScanStringsInChunkOrder(partialResults, merged);

        Assert.Equal([B, Same, A, C], merged[ElementId]);
    }

    [Fact]
    public void ResolveElementName_AfterReverseCompletion_PicksFirstSourceChunkCandidate()
    {
        var state = new ParsingState();
        RvtFileReader.MergeElemScanStringsInChunkOrder(
            ReverseCompletedResults(), state.ElemScanStrings);

        Assert.Equal(B, ElementBuilder.ResolveElementName(ElementId, state));
    }

    private static Dictionary<uint, List<string>>?[] ReverseCompletedResults()
    {
        var partialResults = new Dictionary<uint, List<string>>?[2];

        // Simulate chunk 1 finishing before chunk 0. Indexed storage must still make the
        // merge consume chunk 0 first, preserving the serialized source order.
        partialResults[1] = Partial(A, Same, C);
        partialResults[0] = Partial(B, Same);

        return partialResults;
    }

    private static Dictionary<uint, List<string>> Partial(params string[] strings) =>
        new() { [ElementId] = [.. strings] };
}
