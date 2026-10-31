using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallCutoutStepTests
{
    [Fact]
    public void ShadowRetainsItsOwnGeneratorAndDelegatedWallAndGenerator()
    {
        var steps = Read(Block("shadow"), out var failure)!;
        Assert.Null(failure);
        Assert.Equal(4155, steps[2].ClassId);
        Assert.Equal(902, steps[2].GeneratorId);
        Assert.Equal(300, steps[2].OtherWallId);
        Assert.Equal(901, steps[2].OtherGeneratorId);
        Assert.Null(steps[0].OtherWallId);
    }

    [Fact]
    public void OwnCrossSectionRetainsRawZeroInsteadOfSubstitutingVerticalOne()
    {
        byte[] bytes = Block();
        Assert.Equal(0, TfBlockDeserializer.ExtractOwnWallCrossSection(bytes, 0, BitConverter.ToUInt32(bytes, 8), 3524));
        bytes[^1] ^= 1;
        Assert.Null(TfBlockDeserializer.ExtractOwnWallCrossSection(bytes, 0, BitConverter.ToUInt32(bytes, 8), 3524));
    }

    [Fact]
    public void SelectionUsesLastMatchingInstanceInPhaseFour_NotFlagsOrGeneratorId()
    {
        byte[] bytes = Block();
        var steps = Read(bytes, out string? failure)!;
        Assert.Null(failure);
        Assert.Equal(new[] { 600, 700, 600 }, steps.Select(step => step.InstanceId));
        Assert.Equal(new[] { 0, 1, 2 }, steps.Select(step => step.PhaseIndex));
        var selected = TfBlockDeserializer.FindOwnWallCutoutStep(steps, 600)!;
        Assert.Same(steps[2], selected);
        Assert.Equal(0, selected.CutSomething);
        Assert.Equal(0, selected.IsTrueBoolean);
        Assert.Equal(1, selected.UseUnattachedVoids);
        Assert.Equal(1, selected.RemovedWholeGeometry);
        Assert.Equal(new byte[] { 1, 0, 1 }, selected.TrueBooleanArray);
        Assert.Equal(3, selected.End - selected.Start - 23);
        Assert.Same(steps[1], TfBlockDeserializer.FindOwnWallCutoutStep(steps, 700));
        Assert.Null(TfBlockDeserializer.FindOwnWallCutoutStep(steps, 800));
        Assert.Null(TfBlockDeserializer.FindOwnWallCutoutStep(steps, 0));
    }

    [Theory]
    [InlineData("null-array")] [InlineData("empty-array")]
    public void NativeBooleanArraySentinelDoesNotChangeStepMembership(string mutation)
    {
        var steps = Read(Block(mutation), out var failure)!;
        Assert.Null(failure);
        if (mutation == "null-array") Assert.Null(steps[2].TrueBooleanArray);
        else Assert.Empty(steps[2].TrueBooleanArray!);
        Assert.Same(steps[2], TfBlockDeserializer.FindOwnWallCutoutStep(steps, 600));
    }

    [Theory]
    [InlineData("footer")] [InlineData("foreign-owner")] [InlineData("external-step")]
    [InlineData("version")] [InlineData("short-tail")] [InlineData("array-count")]
    public void MalformedOrUnconfirmedInputsDoNotYieldASelectedStep(string mutation)
    {
        Assert.Null(Read(Block(mutation), out string? failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void SpansRemainFrameRelativeWhenEmbedded()
    {
        byte[] bytes = Block(), embedded = new byte[bytes.Length + 13]; bytes.CopyTo(embedded, 7);
        var actual = TfBlockDeserializer.ExtractOwnWallCutoutSteps(embedded, 7,
            BitConverter.ToUInt32(bytes, 8), 3524, out var failure);
        Assert.Null(failure);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Read(bytes, out _)),
            System.Text.Json.JsonSerializer.Serialize(actual));
    }

    private static List<TfBlockDeserializer.OwnWallCutoutStep>? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallCutoutSteps(bytes, 0, BitConverter.ToUInt32(bytes, 8), 3524, out failure);

    private static byte[] Block(string? mutation = null)
    {
        var (root, offsets) = WallGeometryPhaseTests.Block(3524);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(root.AsSpan(0, offsets["list"]));
        for (int phase = 0; phase < 6; phase++)
        {
            writer.Write(phase == 3 ? 3 : 0);
            if (phase != 3) continue;
            for (int index = 0; index < 3; index++)
            { writer.Write(uint.MaxValue); writer.Write(mutation == "external-step" ? (ushort)0
                : mutation == "shadow" && index == 2 ? (ushort)4155 : (ushort)4137); }
        }
        writer.Write(root.AsSpan(offsets["list"] + 24, offsets["list-end"] - offsets["list"] - 24));
        for (int index = 0; index < 3; index++)
        {
            writer.Write(new byte[16]); writer.Write(900 + index);
            writer.Write(mutation == "version" ? 132 : mutation == "shadow" && index == 2 ? 1 : 133);
            writer.Write(0); writer.Write(2u);
            if (mutation == "short-tail" && index == 2) break;
            writer.Write(index == 2 ? 0 : 1); writer.Write(index == 1 ? 700 : 600);
            writer.Write(-1); writer.Write(-1);
            writer.Write(index == 2 ? (byte)0 : (byte)1); writer.Write(index == 2 ? (byte)1 : (byte)0);
            writer.Write((byte)1);
            writer.Write(mutation == "array-count" ? 100_001u
                : mutation == "null-array" ? uint.MaxValue : mutation == "empty-array" ? 0u : 3u);
            if (mutation is not ("array-count" or "null-array" or "empty-array")) writer.Write(new byte[] { 1, 0, 1 });
            if (mutation == "shadow" && index == 2) { writer.Write(300); writer.Write(901); }
        }
        writer.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        if (mutation == "footer") bytes[^1] ^= 1;
        if (mutation == "foreign-owner") bytes[0] ^= 1;
        return bytes;
    }
}
