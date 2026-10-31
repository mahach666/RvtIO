using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CornerMullionFieldTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(0, 1)] [InlineData(1, 1)] [InlineData(3, 3)]
    public void NeighborCornerArraysReadInlineMullionsAndVariableIdLists(int mullions, int edges)
    {
        Complete(588, Owner(mullions, edges));
        Assert.Null(TfBlockDeserializer.SerializationFields(588)![1].Ref);
        Assert.Equal(589, TfBlockDeserializer.SerializationFields(589)![1].Ref);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void StandaloneEdgeValuesUseTheSameConcreteIdVector(int count)
        => Complete(589, Bytes(w => Edge(w, count)));

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void IdentifierAndEdgeTagBitsAreNotObjectMarkers(int id)
        => Complete(589, Bytes(w => { w.Write(id); w.Write(2u); w.Write(id); w.Write(id); }));

    [Fact]
    public void NullOuterAndInnerSentinelsAreNotRecursiveValues()
    {
        Complete(588, Bytes(w => { w.Write(uint.MaxValue); w.Write(uint.MaxValue); }));
        Complete(589, Bytes(w => { w.Write(-1); w.Write(uint.MaxValue); }));
        Complete(588, Bytes(w => { w.Write(0u); w.Write(1u); w.Write(-1); w.Write(uint.MaxValue); }));
    }

    [Theory]
    [InlineData(588)] [InlineData(589)]
    public void EveryPayloadTruncationFailsWithAValidReframedFooter(int cls)
    {
        var payload = cls == 588 ? Owner(2, 2) : Bytes(w => Edge(w, 3));
        for (int length = 0; length < payload.Length; length++)
        {
            var bytes = Block(cls, payload[..length]);
            Assert.Null(Read(bytes)); Assert.NotNull(Trace(bytes).Failure);
        }
    }

    [Fact]
    public void CountsBeyondTheExistingLimitAreRejectedAtEveryContainer()
    {
        Assert.Null(Read(Block(588, BitConverter.GetBytes(1_000_001u))));
        Assert.Null(Read(Block(588, Bytes(w => { w.Write(0u); w.Write(1_000_001u); }))));
        Assert.Null(Read(Block(589, Bytes(w => { w.Write(17); w.Write(1_000_001u); }))));
    }

    [Fact]
    public void IdentifierBytesAreNotNativePointersOrPadding()
    {
        var bytes = Block(589, Bytes(w => { w.Write(42); w.Write(3u); w.Write(-1); w.Write(7); w.Write(int.MinValue); }));
        var trace = Trace(bytes);
        Assert.Null(trace.Failure); Assert.Equal(20, trace.End - 14); Assert.Empty(trace.Queued);
        Assert.Equal(12, Assert.Single(trace.Fields, f => f.Name == "m_mullionIds").End
            - Assert.Single(trace.Fields, f => f.Name == "m_mullionIds").Start - 4);
    }

    [Fact]
    public void NonzeroFrameStartAndInvalidFooterAreChecked()
    {
        var bytes = Block(588, Owner(1, 1)); var container = new byte[bytes.Length + 17]; bytes.CopyTo(container, 9);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(container, 9, BitConverter.ToUInt32(bytes, 8), 588));
        bytes[^1] ^= 1; Assert.Null(Read(bytes));
    }

    private static byte[] Owner(int mullions, int edges) => Bytes(w =>
    {
        w.Write((uint)mullions); for (int i = 0; i < mullions; i++) { w.Write(-1 - i); w.Write(int.MinValue + i); }
        w.Write((uint)edges); for (int i = 0; i < edges; i++) Edge(w, i);
    });
    private static void Edge(BinaryWriter w, int count)
    {
        w.Write(-1); w.Write((uint)count); for (int i = 0; i < count; i++) w.Write(int.MinValue + i);
    }
    private static void Complete(int cls, byte[] payload)
    {
        var bytes = Block(cls, payload); var result = Read(bytes);
        Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        var trace = Trace(bytes); Assert.Null(trace.Failure);
        Assert.Equal(bytes.Length - 4, trace.End); Assert.Empty(trace.Queued);
    }
    private static DeferredFieldProbe.Result Trace(byte[] bytes)
        => DeferredFieldProbe.Read(bytes, 14, bytes.Length - 4, BitConverter.ToUInt16(bytes, 12), BitConverter.ToUInt16(bytes, 12));
    private static TfBlockDeserializer.NativeParams? Read(byte[] bytes)
        => TfBlockDeserializer.ExtractParams(bytes, 0, BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt16(bytes, 12));
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
    private static byte[] Block(int cls, byte[] payload) => Bytes(w =>
    {
        uint size = (uint)payload.Length + 2;
        w.Write(100); w.Write(0u); w.Write(size); w.Write((ushort)cls); w.Write(payload); w.Write(size);
    });
}
