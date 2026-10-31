using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallGeometryHistoryTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void RetainsAllFourTablesAndOpaqueBaseScalarsWithoutNativeCanonicalization(int cls)
    {
        var (bytes, _) = Block((ushort)cls);
        var result = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(result.Complete); Assert.Null(result.Failure);
        var step = Assert.Single(result.Steps);
        Assert.Equal(2, step.QueueIndex); Assert.Equal(2262, step.ClassId);
        Assert.Equal(99, step.Id); Assert.Equal(-87, step.Version); Assert.Equal(int.MinValue, step.Flags);
        Assert.Equal(42u, step.OwnerReference); // not interpreted as ElementId / resolved alias
        Assert.Equal(new uint[] { 3, 1, 1, 1 }, step.Tables.Select(t => t.Count));
        var face = step.Tables[0];
        Assert.Equal(new[] { 9, -1, 9 }, face.Entries.Select(e => e.Id));
        Assert.Equal(new[] { 5, 12, -1 }, face.Entries[0].Keys);
        Assert.Equal(face.Entries[0].Keys, face.Entries[2].Keys); // duplicate keys/IDs remain
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, step.Tables[1].Entries[0].Keys);
        Assert.All(step.Tables, t => Assert.Equal(t.Start + 4, t.Entries[0].Start));
        Assert.All(face.Entries, e => Assert.Equal(16, e.End - e.Start));
        Assert.Equal(24, step.Tables[1].Entries[0].End - step.Tables[1].Entries[0].Start);
        Assert.Equal(20, step.Tables[2].Entries[0].End - step.Tables[2].Entries[0].Start);
        Assert.Equal(step.End, result.End);
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(uint.MaxValue)]
    public void BackReferenceRemainsRaw(uint value)
    {
        var (bytes, offsets) = Block(3524); BitConverter.GetBytes(value).CopyTo(bytes, offsets["owner"]);
        Assert.Equal(value, Read(bytes, out _)!.Steps[0].OwnerReference);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void NullTableSentinelIsDifferentFromEmpty(int index)
    {
        var (bytes, _) = Block(3524, nullTable: index);
        var step = Assert.Single(Read(bytes, out _)!.Steps);
        Assert.Equal(uint.MaxValue, step.Tables[index].Count); Assert.Empty(step.Tables[index].Entries);
        Assert.Equal(4, step.Tables[index].End - step.Tables[index].Start);
    }

    [Theory]
    [InlineData("count")] [InlineData("rows")] [InlineData("base")]
    public void MalformedHistoryIsExplicitlyIncompleteNotAnExtrudableEmptyStep(string mutation)
    {
        var (bytes, _) = Block(3524, mutation);
        var value = Read(bytes, out var failure)!;
        Assert.False(value.Complete); Assert.NotNull(failure); Assert.Equal(failure, value.Failure);
        Assert.Empty(value.Steps);
    }

    [Theory]
    [InlineData("null")] [InlineData("external")] [InlineData("handle")] [InlineData("wrong-class")]
    public void UnconfirmedPhasePointerIsRetainedAndNotPromoted(string mutation)
    {
        var (bytes, _) = Block(3524, mutation);
        var value = Read(bytes, out string? failure)!;
        Assert.False(value.Complete); Assert.StartsWith("phase-pointer:", failure); Assert.Empty(value.Steps);
        Assert.Single(value.List.Phases[2].Members);
    }

    [Fact]
    public void UnconfirmedIntermediateTailPreservesEarlierPrefixAndStopsAtItsExactBoundary()
    {
        var (bytes, offsets) = Block(3524, "two-steps");
        var value = Read(bytes, out string? failure)!;
        Assert.False(value.Complete); Assert.Equal("step-tail:2:2262:v-87", failure);
        Assert.Equal(offsets["prefix-end"], value.End); Assert.Single(value.Steps);
        Assert.Equal(3, value.TargetQueueIndex);
    }

    [Fact]
    public void ConfirmedIntermediateBodyAllowsNextPrefixWithoutScanning()
    {
        var (bytes, _) = Block(3524, "valid-two-steps");
        var value = Read(bytes, out string? failure)!;
        Assert.Null(failure); Assert.True(value.Complete); Assert.Equal(2, value.Steps.Count);
        Assert.Equal(new[] { 2, 3 }, value.Steps.Select(s => s.QueueIndex));
        Assert.Equal(99, value.Steps[1].Id);
    }

    [Fact]
    public void ConcreteSchemaWithNoOwnFieldsNeedsNoGuessedVersionedTail()
    {
        var (bytes, _) = Block(3524, "no-own-two-steps");
        var value = Read(bytes, out string? failure)!;
        Assert.Null(failure); Assert.True(value.Complete); Assert.Equal(2, value.Steps.Count);
        Assert.Equal(4153, value.Steps[0].ClassId); Assert.Equal(-87, value.Steps[0].Version);
    }

    [Fact]
    public void PriorGNodeConsumesInlineFourScalarGInfoNotAnOwnedPointer()
    {
        var (bytes, _) = Block(3524, "history-face");
        var value = Read(bytes, out string? failure)!;
        Assert.Null(failure); Assert.True(value.Complete);
        var prior = Assert.Single(value.PriorObjects);
        Assert.Equal(1589, prior.ClassId); Assert.Equal(48, prior.End-prior.Start);
        Assert.Equal(3, Assert.Single(value.Steps).QueueIndex);
    }

    [Fact]
    public void PhaseSixAndLaterUnknownBytesAreOutsideTheRequestedPrefix()
    {
        var (bytes, offsets) = Block(3524, "phase-six");
        var value = Read(bytes, out var failure)!;
        Assert.Null(failure); Assert.True(value.Complete); Assert.Single(value.Steps);
        Assert.Equal(offsets["prefix-end"], value.End); Assert.Single(value.List.Phases[5].Members);
    }

    [Fact]
    public void NonzeroFrameStartLeavesAllReportedSpansFrameRelative()
    {
        var (bytes, _) = Block(361); var embedded = new byte[bytes.Length+12]; bytes.CopyTo(embedded, 7);
        var value = TfBlockDeserializer.ExtractOwnWallGeometryHistories(embedded, 7,
            BitConverter.ToUInt32(bytes,8), 361, out var failure);
        Assert.Null(failure);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Read(bytes,out _)),
            System.Text.Json.JsonSerializer.Serialize(value));
    }

    [Theory]
    [InlineData("owner")] [InlineData("footer")] [InlineData("null-document")]
    public void InvalidOwnRootReturnsNoHistories(string mutation)
    {
        var (bytes, _) = WallGeometryPhaseTests.Block(3524,mutation);
        Assert.Null(Read(bytes, out string? failure)); Assert.NotNull(failure);
    }

    private static TfBlockDeserializer.OwnWallGeometryHistories? Read(byte[] bytes,out string? failure)
        => TfBlockDeserializer.ExtractOwnWallGeometryHistories(bytes,0,BitConverter.ToUInt32(bytes,8),
            BitConverter.ToUInt16(bytes,12),out failure);

    private static (byte[] Bytes,Dictionary<string,int> Offsets) Block(ushort cls,string? mutation=null,int? nullTable=null)
    {
        var (original, old) = WallGeometryPhaseTests.Block(cls, mutation=="history-face" ? mutation : null);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(original.AsSpan(0,old["list"])); var offsets = new Dictionary<string,int>();
        for (int phase=0;phase<6;phase++)
        {
            bool selected=phase==2 || mutation=="phase-six" && phase==5;
            int count=selected ? phase==2 && mutation is "two-steps" or "valid-two-steps" or "no-own-two-steps" ? 2 : 1 : 0;
            writer.Write(count);
            for (int index=0;index<count;index++)
            {
                if (phase==2 && index==0) offsets["pointer"]=(int)stream.Position;
                writer.Write(mutation=="null" ? 0u : mutation=="handle" ? 8u : uint.MaxValue);
                if (mutation!="null") writer.Write(mutation=="external" ? (ushort)0
                    : mutation=="wrong-class" ? (ushort)3905
                    : mutation=="no-own-two-steps" && index==0 ? (ushort)4153 : (ushort)2262);
            }
        }
        writer.Write(original.AsSpan(old["list"]+24, old["list-end"]-old["list"]-24));
        if (mutation=="history-face")
        {
            writer.Write(7);writer.Write(0);writer.Write(-2000011);writer.Write(0); // current inline GInfo
            writer.Write(new byte[24]);writer.Write(-1);writer.Write(0u); // complete GFace / Face
        }
        Prefix();
        if (mutation=="valid-two-steps")
        {
            writer.Write(1u);writer.Write(123);writer.Write(1);writer.Write(0); // exact JoinToRoof v16 tail
            Prefix();
        }
        else if (mutation=="no-own-two-steps") Prefix();
        writer.Write(new byte[] {0xff,0xde,0xad,0xbe,0xef});writer.Write(0u);
        byte[] bytes=stream.ToArray();uint size=(uint)bytes.Length-16;
        BitConverter.GetBytes(size).CopyTo(bytes,8);BitConverter.GetBytes(size).CopyTo(bytes,bytes.Length-4);
        return (bytes,offsets);

        void Prefix()
        {
            int[][] rows=[[9,5,12,-1],[-1,int.MinValue,4,0],[9,5,12,-1]];
            for (int table=0;table<4;table++)
            {
                uint count=table==nullTable ? uint.MaxValue : table==3 && mutation is "two-steps" or "valid-two-steps"
                    ? 0u : table==0 ? 3u : 1u;
                writer.Write(mutation=="count" && table==0 ? 100_001u : count);
                if (mutation=="count") return;
                if (count is 0 or uint.MaxValue) continue;
                if (mutation=="rows") return;
                if (table==0) foreach(var row in rows) foreach(int n in row) writer.Write(n);
                else foreach(int n in table==1 ? new[] {7,1,2,3,4,5} : new[] {8,4,3,2,1}) writer.Write(n);
            }
            if (mutation=="base") return;
            writer.Write(99);writer.Write(mutation=="valid-two-steps" ? 16 : -87);writer.Write(int.MinValue);
            offsets["owner"]=(int)stream.Position;writer.Write(42u);offsets["prefix-end"]=(int)stream.Position;
        }
    }
}
