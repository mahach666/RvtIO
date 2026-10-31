using Xunit;

namespace RvtIO.Tests;

public class RootProbeHistoryBoundaryTests
{
    [Theory]
    [InlineData(0u, 4)]
    [InlineData(uint.MaxValue, 4)]
    [InlineData(2u, 28)]
    public void TripletResearchGrammarConsumesExactCountOrEmptySentinel(uint count, int expected)
    {
        byte[] bytes = new byte[32];
        BitConverter.GetBytes(count).CopyTo(bytes, 0);
        Assert.True(HistoryBoundaryProbe.TryTripletContainerEnd(bytes, 0, bytes.Length, out int end));
        Assert.Equal(expected, end);
    }

    [Theory]
    [InlineData(2u, 27)]
    [InlineData(100001u, 32)]
    [InlineData(0u, 3)]
    public void IncompleteOrUnboundedTripletContainerIsRejected(uint count, int limit)
    {
        byte[] bytes = new byte[32];
        BitConverter.GetBytes(count).CopyTo(bytes, 0);
        Assert.False(HistoryBoundaryProbe.TryTripletContainerEnd(bytes, 0, limit, out int end));
        Assert.Equal(-1, end);
    }

    [Theory]
    [InlineData(0u, 2)]
    [InlineData(uint.MaxValue, 2)]
    [InlineData(1u, 1)]
    public void EmptyCurveAliasesShareTailButNonemptyCountedRecordsKeepTheirSize(
        uint curveCount, int layouts)
    {
        byte[] bytes = CoincidentFaceEnd(curveCount);
        var candidates = HistoryBoundaryProbe.VertCompoundStepCandidates(bytes, 0, bytes.Length);
        Assert.Equal(layouts, candidates.Count);
        Assert.Equal(bytes.Length - 24, Assert.Single(candidates.Select(x => x.TailStart).Distinct()));
        if (curveCount == 1) Assert.Equal(24, Assert.Single(candidates).CurveSize);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    [InlineData(1u)]
    public void InvalidOwnerIsNotAcceptedThroughAnAlternativeLayout(uint curveCount)
    {
        byte[] bytes = CoincidentFaceEnd(curveCount, owner: 99);
        Assert.Empty(HistoryBoundaryProbe.VertCompoundStepCandidates(bytes, 0, bytes.Length));
    }

    [Fact]
    public void DifferentFaceEndsAreRecordedWithoutSelectingALayout()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Write(writer, 4, 5, 0, 1, 125); // high-water id and one face entry
        Write(writer, 0, 0, 0, 0); // face terminator
        Write(writer, 0, 0); // empty curve/edge; reverse omitted
        WriteTail(writer);
        byte[] bytes = stream.ToArray();

        var candidates = HistoryBoundaryProbe.VertCompoundStepCandidates(bytes, 0, bytes.Length);
        Assert.Equal(new[] { 32, 36 }, candidates.Select(x => x.FaceEnd).Order().ToArray());
        Assert.Equal(44, Assert.Single(candidates.Select(x => x.TailStart).Distinct()));
    }

    [Fact]
    public void MultipleValidTailsAreKeptAndNotRankedByPositionOrLayout()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Write(writer, 0, 0, 0); // empty face/curve/edge
        WriteTail(writer); // valid if reverse is omitted
        WriteTail(writer); // valid after a counted/terminated reverse table
        byte[] bytes = stream.ToArray();

        var candidates = HistoryBoundaryProbe.VertCompoundStepCandidates(bytes, 0, bytes.Length);
        Assert.Equal(new[] { 12, 36 }, candidates.Select(x => x.TailStart).Distinct().Order().ToArray());
    }

    [Fact]
    public void TruncatedTailCannotBecomeCompleteByChangingLayout()
    {
        byte[] bytes = CoincidentFaceEnd(0);
        for (int end = 0; end < bytes.Length; end++)
            Assert.Empty(HistoryBoundaryProbe.VertCompoundStepCandidates(bytes, 0, end));
    }

    [Theory]
    [InlineData(-1, 56)]
    [InlineData(int.MaxValue, 56)]
    [InlineData(0, -1)]
    [InlineData(0, int.MaxValue)]
    public void InvalidBoundsReturnNoCandidates(int start, int end)
        => Assert.Empty(HistoryBoundaryProbe.VertCompoundStepCandidates(
            CoincidentFaceEnd(0), start, end));

    [Fact]
    public void NonfiniteMinimumHeightDoesNotConfirmAStepTail()
    {
        byte[] bytes = CoincidentFaceEnd(0);
        BitConverter.GetBytes(double.NaN).CopyTo(bytes, bytes.Length - 8);
        Assert.Empty(HistoryBoundaryProbe.VertCompoundStepCandidates(bytes, 0, bytes.Length));
    }

    [Fact]
    public void CurrentStairCurvesAfterEmptyFaceAreAnAdditionalRecordedLayout()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        Write(writer, 0, 1, 7, 0, 0, 0, 0, 0); // empty face + one current 24-byte curve
        Write(writer, 0, 0); WriteTail(writer);
        byte[] bytes = stream.ToArray();
        Assert.Empty(HistoryBoundaryProbe.VertCompoundStepCandidates(bytes, 0, bytes.Length));
        var candidate = Assert.Single(HistoryBoundaryProbe.StairStepCandidates(bytes, 0, bytes.Length));
        Assert.Equal(24, candidate.CurveSize);
        Assert.Equal(bytes.Length - 24, candidate.TailStart);
        for (int end = 0; end < bytes.Length; end++)
            Assert.Empty(HistoryBoundaryProbe.StairStepCandidates(bytes, 0, end));
    }

    [Fact]
    public void StairVariantPreservesDistinctValidTailsWithoutSelectingOne()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        Write(writer, 0, 0, 0); WriteTail(writer); WriteTail(writer);
        byte[] bytes = stream.ToArray();
        Assert.Equal(new[] { 12, 36 }, HistoryBoundaryProbe.StairStepCandidates(bytes, 0, bytes.Length)
            .Select(candidate => candidate.TailStart).Distinct().Order().ToArray());
    }

    private static byte[] CoincidentFaceEnd(uint curveCount, int owner = 2)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Write(writer, 1, 30, 0, 0, 0); // counted entry / prefixed terminator
        writer.Write(curveCount);
        if (curveCount == 1) Write(writer, 1, 0, 0, 0, 0, 0); // 24-byte curve
        Write(writer, 0, 0); // empty edge/reverse
        WriteTail(writer, owner);
        return stream.ToArray();
    }

    private static void WriteTail(BinaryWriter writer, int owner = 2)
    {
        Write(writer, 1, 0, 0, owner); // id/version/flags/own-root handle
        writer.Write(0.0); // minimumWallHeight
    }

    private static void Write(BinaryWriter writer, params int[] values)
    {
        foreach (int value in values) writer.Write(value);
    }
}
