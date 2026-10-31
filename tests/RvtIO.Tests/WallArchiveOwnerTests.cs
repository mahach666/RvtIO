using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallArchiveOwnerTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void FreshDocumentContextLinksListAndOrderedStepsToOwnRoot(int cls)
    {
        var (bytes, end) = WallGeometrySixPhaseTests.Block(500, new byte[4], firstBase: true, root: (ushort)cls);
        var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.Equal(100, value.OwnerId);
        Assert.Equal(1u, value.ExternalPointerCount); Assert.Equal(2u, value.RootArchiveKey);
        Assert.Equal(2086, value.List.ClassId); Assert.Equal(1, value.List.QueueIndex);
        Assert.Equal(new[] { 2, 3 }, value.Steps.Select(step => step.QueueIndex));
        Assert.All(value.Steps.Prepend(value.List), link =>
        { Assert.Equal(2u, link.Reference); Assert.Equal(100, link.RootOwnerId); });
        Assert.Equal(end + 4, value.End);
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(3u)]
    [InlineData(42u)] [InlineData(100u)] [InlineData(uint.MaxValue)]
    public void RawListReferencesAreNeverSubstitutedOrInterpretedAsElementIds(uint reference)
    {
        var (bytes, offsets) = WallGeometryPhaseTests.Block(3524);
        BitConverter.GetBytes(reference).CopyTo(bytes, offsets["owner-reference"]);
        var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.Equal(reference, value.List.Reference);
        Assert.Equal(reference == 2 ? 100 : (int?)null, value.List.RootOwnerId);
        Assert.Empty(value.Steps);
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(3u)]
    [InlineData(100u)] [InlineData(uint.MaxValue)]
    public void EachStepReferenceIsResolvedIndependentlyWithoutChangingEarlierLinks(uint reference)
    {
        var (bytes, end) = WallGeometrySixPhaseTests.Block(500, new byte[4], firstBase: true);
        BitConverter.GetBytes(reference).CopyTo(bytes, end - 4);
        var value = Read(bytes, out var failure)!; Assert.Null(failure);
        Assert.Equal(100, value.List.RootOwnerId); Assert.Equal(100, value.Steps[0].RootOwnerId);
        Assert.Equal(reference, value.Steps[1].Reference);
        Assert.Equal(reference == 2 ? 100 : (int?)null, value.Steps[1].RootOwnerId);
    }

    [Theory]
    [InlineData("owner")] [InlineData("zero-owner")] [InlineData("negative-owner")]
    [InlineData("footer")] [InlineData("size")] [InlineData("class")]
    [InlineData("null-document")] [InlineData("foreign-document")]
    [InlineData("null-list")] [InlineData("external-list")] [InlineData("wrong-list")]
    public void UnconfirmedRootOrContextDoesNotProduceOwnerLinks(string mutation)
    {
        var (bytes, _) = WallGeometryPhaseTests.Block(3524, mutation);
        Assert.Null(Read(bytes, out var failure)); Assert.False(string.IsNullOrEmpty(failure));
    }

    [Theory]
    [InlineData("null")] [InlineData("external")] [InlineData("handle")] [InlineData("wrong-class")]
    public void IncompletePhasePrefixDoesNotProduceOwnerLinks(string pointer)
    {
        var (bytes, _) = WallGeometrySixPhaseTests.Block(500, new byte[4], pointer: pointer);
        Assert.Null(Read(bytes, out var failure)); Assert.StartsWith("phase-pointer:", failure);
    }

    [Fact]
    public void ExactPrefixSupportsNonzeroFrameStartWithoutClaimingTheLaterFifo()
    {
        var (bytes, _) = WallGeometryPhaseTests.Block(361);
        byte[] segment = new byte[bytes.Length + 21]; bytes.CopyTo(segment, 9);
        var actual = TfBlockDeserializer.ExtractOwnWallArchiveOwnerLinks(segment, 9,
            BitConverter.ToUInt32(bytes, 8), 361, out var failure);
        Assert.Null(failure);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Read(bytes, out _)),
            System.Text.Json.JsonSerializer.Serialize(actual));
        Assert.True(actual!.End < bytes.Length - 4);
    }

    private static TfBlockDeserializer.OwnWallArchiveOwnerLinks? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallArchiveOwnerLinks(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out failure);
}
