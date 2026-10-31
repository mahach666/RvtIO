using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The two layouts that stopped the walk of the deferred objects of the sample project P fittings before their connector manager:
/// the extensible-storage entity map of ESEntityCell and the extra string of MEPAnalyticalFittingData.</summary>
public class TfExtensibleStorageEntityTests
{
    private static readonly byte[] SchemaGuid =
        [0x2B, 0x2A, 0x02, 0x1B, 0x22, 0x57, 0x87, 0x47, 0x92, 0x8A, 0x4F, 0x29, 0xCA, 0x9D, 0x88, 0x11];

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);

    private static byte[] Entity(byte[]? secondGuid = null, uint marker = 0xFFFFFFFF) =>
        Concat(SchemaGuid, U32(marker), secondGuid ?? SchemaGuid);

    [Fact]
    public void EntityMap_OneEmptyEntityIsSkipped()
    {
        byte[] data = Concat([0xAA], U32(1), Entity(), [0x01]);

        Assert.Equal(1 + 4 + 36, TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(data, 1, data.Length));
    }

    [Fact]
    public void EntityMap_SeveralEmptyEntitiesAreSkipped()
    {
        byte[] data = Concat(U32(2), Entity(), Entity());

        Assert.Equal(4 + 2 * 36, TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(data, 0, data.Length));
    }

    [Fact]
    public void EntityMap_AnyOtherLayoutIsLeftToTheGenericReader()
    {
        byte[] otherGuid = (byte[])SchemaGuid.Clone();
        otherGuid[15] ^= 1;
        Assert.Null(TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(Concat(U32(1), Entity(otherGuid)), 0, 40));
        Assert.Null(TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(Concat(U32(1), Entity(marker: 0)), 0, 40));
        Assert.Null(TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(Concat(U32(0), Entity()), 0, 40));
        Assert.Null(TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(Concat(U32(17), Entity()), 0, 40));
    }

    [Fact]
    public void EntityMap_ATruncatedEntityIsNotSkipped()
    {
        byte[] data = Concat(U32(1), Entity());

        Assert.Null(TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(data, 0, data.Length - 1));
        Assert.Null(TfBlockDeserializer.SkipEmptyExtensibleStorageEntities(data, data.Length - 2, data.Length));
    }

    private static byte[] ConnectorArray(int count, ushort tag = 2366) =>
        Concat(U32((uint)count), Concat(Enumerable.Range(0, count).Select(_ => Concat(U32(0xFFFFFFFF), BitConverter.GetBytes(tag))).ToArray()));

    private static byte[] Utf16(string text) => Concat(U32((uint)text.Length), System.Text.Encoding.Unicode.GetBytes(text));

    [Fact]
    public void FittingData_ArrayRightAtTheCursorStaysWhereItIs()
    {
        byte[] data = Concat([0xAA, 0xBB], ConnectorArray(3), U32(0));

        Assert.Equal(2, TfBlockDeserializer.SkipExtraFittingDataString(data, 2, data.Length));
    }

    [Fact]
    public void FittingData_OneExtraStringBeforeTheArrayIsSkipped()
    {
        byte[] extra = Utf16("Tee");
        byte[] data = Concat([0xAA], extra, ConnectorArray(3), U32(0));

        Assert.Equal(1 + extra.Length, TfBlockDeserializer.SkipExtraFittingDataString(data, 1, data.Length));
    }

    [Fact]
    public void FittingData_NoArrayAfterOneStringLeavesTheCursor()
    {
        byte[] withWrongTag = Concat(Utf16("Tee"), ConnectorArray(2, tag: 1234));
        Assert.Equal(0, TfBlockDeserializer.SkipExtraFittingDataString(withWrongTag, 0, withWrongTag.Length));

        byte[] twoStrings = Concat(Utf16("Tee"), Utf16("Tee"), ConnectorArray(2));
        Assert.Equal(0, TfBlockDeserializer.SkipExtraFittingDataString(twoStrings, 0, twoStrings.Length));

        byte[] empty = Concat(U32(0), U32(0));
        Assert.Equal(0, TfBlockDeserializer.SkipExtraFittingDataString(empty, 0, empty.Length));
    }

    [Fact]
    public void FittingData_AStringRunningPastTheEndLeavesTheCursor()
    {
        byte[] data = Concat(U32(100), [1, 2, 3]);

        Assert.Equal(0, TfBlockDeserializer.SkipExtraFittingDataString(data, 0, data.Length));
    }
}
