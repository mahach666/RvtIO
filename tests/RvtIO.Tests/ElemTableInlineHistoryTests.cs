using System.Reflection;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class ElemTableInlineHistoryTests
{
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    [InlineData(0x03300331)]
    public void HistoryHasFourLiteralInt32Values(int value)
        => AssertComplete(Block(819, Payload(w => History(w, value))));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(int.MinValue)]
    public void GraveyardContainsIdPartitionAndInlineHistory(int value)
        => AssertComplete(Block(1313, Payload(w => Graveyard(w, value))));

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(0, 1)] [InlineData(3, 3)]
    public void TableReadsBothArraysBeforeTheSourcePointer(int live, int dead)
        => AssertComplete(Block(1312, Table(live, dead)));

    [Theory]
    [InlineData((ushort)819)] [InlineData((ushort)1313)] [InlineData((ushort)1312)]
    public void EveryTruncationRejectsEvenWithAValidReframedFooter(ushort cls)
    {
        byte[] payload = cls == 819 ? Payload(w => History(w, -1))
            : cls == 1313 ? Payload(w => Graveyard(w, -1)) : Table(1, 1);
        for (int count = 0; count < payload.Length; count++) Assert.Null(Read(Block(cls, payload[..count])));
    }

    [Fact]
    public void SourceOwnershipQueueAndFollowingBoolArePreserved()
    {
        var block = Block(1312, Table(1, 1, 44, 2191));
        AssertComplete(block);
        Assert.Equal(new[] { ((ushort)2191, 44u) }, TfBlockDeserializer.TraceQueue(block, 0,
            BitConverter.ToUInt32(block, 8), 1312));
        Assert.Null(Read(Block(1312, Table(1, 1, 44, ushort.MaxValue))));
    }

    [Fact]
    public void ArrayCountAndFooterValidationRemainActive()
    {
        byte[] payload = Table(0, 0); BitConverter.GetBytes(uint.MaxValue - 1).CopyTo(payload, 4);
        Assert.Null(Read(Block(1312, payload)));
        byte[] block = Block(1312, Table(1, 1)); block[^1] ^= 1; Assert.Null(Read(block));
    }

    [Fact]
    public void NonzeroStartDoesNotChangeFieldWidths()
    {
        byte[] block = Block(1312, Table(1, 1)); var padded = new byte[block.Length + 23]; block.CopyTo(padded, 9);
        Assert.NotNull(TfBlockDeserializer.ExtractParams(padded, 9, BitConverter.ToUInt32(block, 8), 1312));
    }

    [Fact]
    public void SharedRecoveredDescriptorsAreNotMutated()
    {
        AssertComplete(Block(1312, Table(1, 1)));
        Assert.Null(TfBlockDeserializer.SerializationFields(1312)![1].Ref);
        Assert.Equal(1313, TfBlockDeserializer.SerializationFields(1313)![2].Ref);
        Assert.Equal(819, TfBlockDeserializer.SerializationFields(819)![3].Ref);
    }

    private static void AssertComplete(byte[] block)
    {
        var result = Read(block); Assert.NotNull(result); Assert.True(result.RootParameterStoresComplete);
        Type type = typeof(TfBlockDeserializer), cursorType = type.GetNestedType("Cursor", BindingFlags.NonPublic)!;
        object cursor = Activator.CreateInstance(cursorType)!;
        cursorType.GetField("D")!.SetValue(cursor, block);
        cursorType.GetField("Pos")!.SetValue(cursor, 14);
        cursorType.GetField("End")!.SetValue(cursor, block.Length - 4);
        var queue = new List<(ushort, uint)>();
        foreach (var field in TfBlockDeserializer.SerializationFields(BitConverter.ToUInt16(block, 12))!)
            type.GetMethod("ReadField", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [cursor, field, queue, 0, null]);
        Assert.Equal(block.Length - 4, (int)cursorType.GetField("Pos")!.GetValue(cursor)!);
    }
    private static TfBlockDeserializer.NativeParams? Read(byte[] b)
        => TfBlockDeserializer.ExtractParams(b, 0, BitConverter.ToUInt32(b, 8), BitConverter.ToUInt16(b, 12));
    private static void History(BinaryWriter w, int value)
    { w.Write(value); w.Write(value); w.Write(value); w.Write(value); }
    private static void Graveyard(BinaryWriter w, int value)
    { w.Write(value); w.Write(value); History(w, value); }
    private static byte[] Table(int live, int dead, uint source = 0, ushort cls = 0)
        => Payload(w =>
        {
            w.Write(live);
            for (int i = 0; i < live; i++) { w.Write(i + 7); History(w, -1); w.Write(0); w.Write(-1); }
            w.Write(dead); for (int i = 0; i < dead; i++) Graveyard(w, i + 7);
            w.Write(source); if (source != 0) w.Write(cls); w.Write((byte)1);
        });
    private static byte[] Payload(Action<BinaryWriter> write)
    { using var s = new MemoryStream(); using var w = new BinaryWriter(s); write(w); return s.ToArray(); }
    private static byte[] Block(ushort cls, byte[] payload)
        => Payload(w => { uint size = (uint)payload.Length + 2; w.Write(100); w.Write(0); w.Write(size); w.Write(cls); w.Write(payload); w.Write(size); });
}
