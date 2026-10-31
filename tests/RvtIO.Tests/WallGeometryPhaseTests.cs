using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallGeometryPhaseTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void CompleteOwnListRetainsEveryPhaseAndFullTail(int classId)
    {
        var (bytes, offsets) = Block((ushort)classId);
        var value = Read(bytes, out string? failure)!;
        Assert.Null(failure);
        Assert.Equal(100, value.OwnerId);
        Assert.Equal(classId, value.ClassId);
        Assert.Equal(offsets["list"], value.Start);
        Assert.Equal(offsets["list-end"], value.End);
        Assert.Equal(1, value.ListPointer.QueueIndex);
        var prior = Assert.Single(value.PriorObjects);
        Assert.Equal(2886, prior.ClassId);
        Assert.Equal(value.RootEnd, prior.Start);
        Assert.Equal(value.Start, prior.End);
        Assert.Equal(new[] { "m_nonBRepGList", "m_bRepFormGList", "m_bRepAdjustGList",
            "m_bRepCutOutGList", "m_bRepPostCutOutGList", "m_bRepTweakGList" }, value.Phases.Select(p => p.Name));
        Assert.All(value.Phases, phase => Assert.Equal(0u, phase.Count));
        Assert.Equal(4, value.Snapshots.Count);
        Assert.Equal(2u, value.OwnerReference);
        Assert.Equal(new[] { 8, 7, -1, 2, 1 }, value.LatestStepTypes);
        Assert.Equal(55, value.IdCounter);
        Assert.Equal(0x81, value.Flags);
    }

    [Fact]
    public void OrderedSlotsRetainNullExternalOwnedAndHandlePointersWithoutGuessingStepValidity()
    {
        var value = Read(Block(3524, "slots").Bytes, out _) !;
        var members = value.Phases[2].Members;
        Assert.Equal(4u, value.Phases[2].Count);
        Assert.Equal(new uint[] { 0, uint.MaxValue, uint.MaxValue, 42 }, members.Select(p => p.Handle));
        Assert.Equal(new ushort?[] { null, 0, 2262, 3905 }, members.Select(p => p.ClassId));
        Assert.Equal(new int?[] { null, null, 2, 3 }, members.Select(p => p.QueueIndex));
        Assert.Equal(new[] { 4, 6, 6, 6 }, members.Select(p => p.End - p.Start));
        Assert.Equal(4, value.Snapshots[0].QueueIndex);
        // A non-GeomStep class is retained as input, not silently dropped or called allowed.
        Assert.Equal((ushort?)3905, members[3].ClassId);
    }

    [Fact]
    public void NullCollectionSentinelIsNotConvertedToEmptyCount()
    {
        var value = Read(Block(3524, "null-array").Bytes, out _) !;
        Assert.Equal(uint.MaxValue, value.Phases[3].Count);
        Assert.Empty(value.Phases[3].Members);
        Assert.Equal(4, value.Phases[3].End - value.Phases[3].Start);
        Assert.Equal(0u, value.Phases[4].Count);
    }

    [Theory]
    [InlineData("ginfo", 8)] [InlineData("extended-ginfo", 64)]
    public void InlineGInfoIsConsumedButNotQueuedOrPromotedToGeomStep(string mutation, int length)
    {
        var pointer = Assert.Single(Read(Block(3524, mutation).Bytes, out _)!.Phases[2].Members);
        Assert.Equal((ushort?)4, pointer.ClassId);
        Assert.Null(pointer.QueueIndex);
        Assert.Equal(length, pointer.End - pointer.Start);
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(42u)] [InlineData(uint.MaxValue)]
    public void OwnerReferenceIsRawNonOwningHandleNotRootElementId(uint reference)
    {
        var (bytes, offsets) = Block(3524);
        BitConverter.GetBytes(reference).CopyTo(bytes, offsets["owner-reference"]);
        Assert.Equal(reference, Read(bytes, out _)!.OwnerReference);
    }

    [Theory]
    [InlineData("owner")] [InlineData("zero-owner")] [InlineData("negative-owner")]
    [InlineData("footer")] [InlineData("size")] [InlineData("class")]
    [InlineData("null-document")] [InlineData("foreign-document")]
    [InlineData("null-list")] [InlineData("external-list")] [InlineData("handle-list")]
    [InlineData("wrong-list")] [InlineData("unknown-list")]
    [InlineData("prior-count")] [InlineData("short-root")]
    [InlineData("short-list")] [InlineData("huge-count")]
    [InlineData("short-pointer")] [InlineData("unknown-phase-class")]
    [InlineData("invalid-ginfo")]
    public void UnconfirmedPrefixReturnsNoResultAndExplicitFailure(string mutation)
    {
        var (bytes, _) = Block(3524, mutation);
        Assert.Null(Read(bytes, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Theory]
    [InlineData(-1, 8u, 3524)] [InlineData(0, 0u, 3524)] [InlineData(0, uint.MaxValue, 3524)]
    [InlineData(0, 8u, 1794)] [InlineData(int.MaxValue, 8u, 3524)]
    public void InvalidFramingArgumentsDoNotThrow(int start, uint size, int classId)
    {
        Assert.Null(TfBlockDeserializer.ExtractOwnWallGeometryPhases(new byte[32], start, size,
            (ushort)classId, out string? failure));
        Assert.Equal("frame", failure);
    }

    [Fact]
    public void PrefixIgnoresUnknownLaterPayloadAndSupportsNonzeroFrameStart()
    {
        var (bytes, _) = Block(361);
        byte[] segment = new byte[bytes.Length + 19]; bytes.CopyTo(segment, 11);
        var actual = TfBlockDeserializer.ExtractOwnWallGeometryPhases(segment,
            11, BitConverter.ToUInt32(bytes, 8), 361, out string? failure);
        Assert.Null(failure);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(Read(bytes, out _)),
            System.Text.Json.JsonSerializer.Serialize(actual));
    }

    private static TfBlockDeserializer.OwnWallGeometryPhases? Read(byte[] bytes, out string? failure)
        => TfBlockDeserializer.ExtractOwnWallGeometryPhases(bytes, 0, BitConverter.ToUInt32(bytes, 8),
            BitConverter.ToUInt16(bytes, 12), out failure);

    internal static (byte[] Bytes, Dictionary<string, int> Offsets) Block(ushort cls, string? mutation = null,
        byte[]? geometryTablePointer = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var offsets = new Dictionary<string, int>();
        writer.Write(100); writer.Write(0u); writer.Write(0u); writer.Write(cls);
        foreach (var field in TfBlockDeserializer.SerializationFields(cls)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(100); continue; }
            if (field.Declaring == 37 && field.Name == "m_docAccess")
            { writer.Write(mutation == "foreign-document" ? 2u : mutation == "null-document" ? 0u : 1u); continue; }
            if (field.Name == "m_pParamValueSetAString") { Pointer(uint.MaxValue, 2886); continue; }
            if (field.Declaring == 37 && field.Name == "m_pGeomTable" && geometryTablePointer != null)
            { writer.Write(geometryTablePointer); continue; }
            if (mutation == "history-face" && field.Name == "m_pGeomTable")
            { Pointer(uint.MaxValue, 1589); continue; }
            if (field.Name == "m_geomSteps")
            {
                if (mutation == "null-list") writer.Write(0u);
                else Pointer(mutation == "handle-list" ? 42u : uint.MaxValue,
                    mutation == "external-list" ? (ushort)0 : mutation == "wrong-list" ? (ushort)3905
                    : mutation == "unknown-list" ? ushort.MaxValue : (ushort)2086);
                continue;
            }
            if (mutation == "short-root" && field.Name == "m_cellList") return Finish();
            Default(field);
        }
        writer.Write(mutation == "prior-count" ? uint.MaxValue : 0u);
        offsets["list"] = (int)stream.Position;
        for (int index = 0; index < 6; index++)
        {
            if (mutation == "short-list" && index == 4) return Finish();
            if (mutation == "null-array" && index == 3) { writer.Write(uint.MaxValue); continue; }
            if (mutation == "huge-count" && index == 3) { writer.Write(1_000_001u); return Finish(); }
            if (index == 2 && mutation is "ginfo" or "extended-ginfo" or "invalid-ginfo")
            {
                writer.Write(1u); Pointer(uint.MaxValue, 4);
                writer.Write(mutation == "invalid-ginfo" ? (ushort)0x0208
                    : mutation == "extended-ginfo" ? (ushort)0x0108 : (ushort)8);
                if (mutation == "extended-ginfo") writer.Write(new byte[56]);
                continue;
            }
            if (index == 2 && mutation is "slots" or "short-pointer" or "unknown-phase-class")
            {
                writer.Write(mutation == "slots" ? 4u : 1u);
                if (mutation == "short-pointer") { writer.Write(uint.MaxValue); return Finish(); }
                if (mutation == "unknown-phase-class") { Pointer(uint.MaxValue, ushort.MaxValue); return Finish(); }
                writer.Write(0u); Pointer(uint.MaxValue, 0); Pointer(uint.MaxValue, 2262); Pointer(42u, 3905);
            }
            else writer.Write(0u);
        }
        if (mutation == "slots") Pointer(uint.MaxValue, 3905); else writer.Write(0u);
        writer.Write(new byte[12]);
        offsets["owner-reference"] = (int)stream.Position;
        writer.Write(2u); foreach (int value in new[] { 8, 7, -1, 2, 1 }) writer.Write(value);
        writer.Write(55); writer.Write((byte)0x81);
        offsets["list-end"] = (int)stream.Position;
        writer.Write(new byte[] { 0xff, 0xde, 0xad, 0xbe, 0xef }); // later graph outside the prefix
        return Finish();

        void Pointer(uint handle, ushort kind) { writer.Write(handle); writer.Write(kind); }
        (byte[], Dictionary<string, int>) Finish()
        {
            writer.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
            BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
            if (mutation == "owner") BitConverter.GetBytes(101).CopyTo(bytes, 0);
            if (mutation == "zero-owner") BitConverter.GetBytes(0).CopyTo(bytes, 0);
            if (mutation == "negative-owner") BitConverter.GetBytes(-1).CopyTo(bytes, 0);
            if (mutation == "size") BitConverter.GetBytes(size - 1).CopyTo(bytes, 8);
            if (mutation == "footer") bytes[^1] ^= 1;
            if (mutation == "class") BitConverter.GetBytes((ushort)1794).CopyTo(bytes, 12);
            return (bytes, offsets);
        }
        void Default(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 == 5) { writer.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int index = 0; index < count; index++) switch (field.Type)
            {
                case 1: case 2: case 3: writer.Write((byte)0); break;
                case 4: case 5: case 6: writer.Write(0); break;
                case 7: writer.Write(0d); break;
                case 8: writer.Write(0u); break;
                case 9: writer.Write(new byte[16]); break;
                case 10: writer.Write(0L); break;
                case 13: writer.Write(new byte[(field.Cnt ?? 1) * 8]); break;
                case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                    foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) Default(nested); break;
                case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(field.Name);
            }
        }
    }
}
