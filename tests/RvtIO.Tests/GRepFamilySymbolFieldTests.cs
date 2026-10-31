using System.Reflection;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class GRepFamilySymbolFieldTests
{
    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(3u)] [InlineData(uint.MaxValue)]
    public void DummyHostFaceTagPairsLeaveTheFollowingFieldAligned(uint count)
    {
        int size = count == uint.MaxValue ? 0 : (int)count * 8;
        byte[] data = new byte[4 + size + 4];
        BitConverter.GetBytes(count).CopyTo(data, 0);
        BitConverter.GetBytes(123456).CopyTo(data, 4 + size);
        Assert.Equal(4 + size, Read(data, 1829, "m_dummyHostInsertionFaceTags"));
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(4)] [InlineData(7)] [InlineData(11)]
    public void TruncatedFaceTagPairFails(int size)
    {
        byte[] data = new byte[12]; BitConverter.GetBytes(1u).CopyTo(data, 0);
        AssertFailure(data[..size], 1829, "m_dummyHostInsertionFaceTags");
    }

    [Fact]
    public void ImpossibleFaceTagCountFails() =>
        AssertFailure(BitConverter.GetBytes(1_000_001u), 1829, "m_dummyHostInsertionFaceTags");

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void EmptyPretaggingNodeArrayHasOnlyItsCount(uint count) =>
        Assert.Equal(4, Read(BitConverter.GetBytes(count), 1829, "m_pretaggingGNodes"));

    [Fact]
    public void PretaggingNodesAreCountedTypedPointers()
    {
        byte[] data = [1,0,0,0,255,255,255,255,204,7]; // one owned class1996 pointer
        Assert.Equal(10, Read(data, 1829, "m_pretaggingGNodes"));
        AssertFailure(data[..9], 1829, "m_pretaggingGNodes");
    }

    [Fact]
    public void SubInstancePairReadsItsDescriptorInsteadOfRecursingIntoItself()
    {
        // Null pointers/containers, empty FamilyParams, identity-sized Trf, IDs/flags:130 bytes.
        Assert.Equal(130, Read(new byte[134], 1763, "second"));
        AssertFailure(new byte[129], 1763, "second");
    }

    private static void AssertFailure(byte[] data, int declaring, string name)
    {
        var error = Assert.Throws<TargetInvocationException>(() => Read(data, declaring, name));
        Assert.Equal("DecodeStop", error.InnerException!.GetType().Name);
    }

    private static int Read(byte[] data, int declaring, string name)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(GRepParserV2).GetNestedType("Cur", BindingFlags.NonPublic)!;
        var cursor = Activator.CreateInstance(type, true)!;
        type.GetField("D", instance)!.SetValue(cursor, data);
        type.GetField("End", instance)!.SetValue(cursor, data.Length);
        type.GetField("RootClass", instance)!.SetValue(cursor, (ushort)1829);
        var field = TfBlockDeserializer.SerializationFields(declaring)!.Single(f => f.Declaring == declaring && f.Name == name);
        typeof(GRepParserV2).GetMethod("ReadField", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [cursor, field, 0]);
        return (int)type.GetField("Pos", instance)!.GetValue(cursor)!;
    }
}
