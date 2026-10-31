using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class FamilyCutNativeIndexTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(-1, 5)] [InlineData(1, 0)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void ExactCutsKeepOrdinalPairingAndRawHostReferenceIndices(int first, int second)
    {
        byte[] bytes = Block([first, second]);
        var loops = TfBlockDeserializer.ExtractOwnFamilySymbolCutLoops(
            bytes, 0, Size(bytes), 1829, out int? count)!;
        Assert.Equal(2, count);
        Assert.Equal(2, loops.Count);
        Assert.Equal(new[] { first, second }, loops.Select(loop => loop.CutIndex));
        Assert.Equal(new[] { 0d, 10d }, loops.Select(loop => loop.Vertices.Min(point => point[0])));
        Assert.Equal(new[] { -1d, 1d }, loops.Select(loop => loop.Direction[1]));
        Assert.All(loops, loop => Assert.Equal(4, loop.Vertices.Count));
    }

    [Fact]
    public void LegacyPermutationConsumerIsNotActivatedByTheCorrection()
    {
        byte[] bytes = Block([1, 0]);
        var loops = TfBlockDeserializer.ExtractFamilySymbolCutLoops(bytes, 0, Size(bytes), 1829)!;
        Assert.Equal(new[] { 10d, 0d }, loops.Select(loop => loop.Vertices.Min(point => point[0])));
        bytes = Block([0, 0]);
        Assert.Null(TfBlockDeserializer.ExtractFamilySymbolCutLoops(bytes, 0, Size(bytes), 1829));
    }

    [Theory]
    [InlineData("direction-count")] [InlineData("index-count")] [InlineData("endpoint-overflow")]
    public void InvalidParallelArraysAndNonfiniteDerivedVerticesDoNotBecomeProfiles(string mutation)
    {
        byte[] bytes = Block([0, 0], mutation);
        Assert.Null(TfBlockDeserializer.ExtractOwnFamilySymbolCutLoops(bytes, 0, Size(bytes), 1829, out int? count));
        Assert.Equal(2, count);
    }

    [Fact]
    public void ArcCutLoopDiscretizesIntoExpectedSampledVertices()
    {
        byte[] bytes = BlockWithArc(hostIndex: 7);
        var loops = TfBlockDeserializer.ExtractOwnFamilySymbolCutLoops(
            bytes, 0, Size(bytes), 1829, out int? count)!;
        Assert.NotNull(loops);
        Assert.Equal(1, count);
        Assert.Single(loops);
        var loop = loops[0];
        Assert.Equal(7, loop.CutIndex);
        // 3 straight lines + 1 semi-circle arc (pi span / (pi/16) = 16 chords) => 19 vertices
        Assert.Equal(19, loop.Vertices.Count);
        Assert.Equal(-1d, loop.Direction[1]);
        Assert.All(loop.Vertices, pt => Assert.All(pt, c => Assert.True(double.IsFinite(c))));
    }

    private static uint Size(byte[] bytes) => BitConverter.ToUInt32(bytes, 8);

    internal static byte[] Block(int[] hostIndices, string? mutation = null, int familyId = 200, byte active = 1)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(100); writer.Write(0u); writer.Write(0u); writer.Write((ushort)1829);
        foreach (var field in TfBlockDeserializer.SerializationFields(1829)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(100); continue; }
            if (field.Declaring == 37 && field.Name == "m_docAccess") { writer.Write(1u); continue; }
            if (field.Declaring == 1829 && field.Name == "m_familyId") { writer.Write(familyId); continue; }
            if (field.Declaring == 1829 && field.Name == "m_active") { writer.Write(active); continue; }
            if (field.Declaring == 1830 && field.Name == "m_pCutLoops")
            { writer.Write(2); Pointer(writer, 938); Pointer(writer, 938); continue; }
            if (field.Declaring == 1830 && field.Name == "m_cutDirs")
            {
                int count = mutation == "direction-count" ? 1 : 2; writer.Write(count);
                for (int index = 0; index < count; index++)
                { writer.Write(0d); writer.Write(index == 0 ? -1d : 1d); writer.Write(0d); }
                continue;
            }
            if (field.Declaring == 1830 && field.Name == "m_cutIndices")
            {
                int count = mutation == "index-count" ? 1 : hostIndices.Length; writer.Write(count);
                foreach (int index in hostIndices.Take(count)) writer.Write(index);
                continue;
            }
            WriteDefault(writer, field);
        }
        for (int loop = 0; loop < 2; loop++)
        { writer.Write((byte)0); writer.Write(4); for (int edge = 0; edge < 4; edge++) Pointer(writer, 1736); }
        for (int loop = 0; loop < 2; loop++)
        {
            double x = loop * 10;
            double[][] vertices = [[x, 0, 0], [x + 1, 0, 0], [x + 1, 0, 2], [x, 0, 2]];
            for (int edge = 0; edge < 4; edge++)
            {
                writer.Write(0); writer.Write(0); writer.Write(-1); writer.Write(0x080004);
                writer.Write(0d); writer.Write(1d);
                for (int axis = 0; axis < 3; axis++)
                    writer.Write(mutation == "endpoint-overflow" && edge == 0 && axis == 0
                        ? double.MaxValue : vertices[edge][axis]);
                for (int axis = 0; axis < 3; axis++)
                    writer.Write(mutation == "endpoint-overflow" && edge == 0 && axis == 0
                        ? double.MaxValue : vertices[(edge + 1) % 4][axis] - vertices[edge][axis]);
            }
        }
        writer.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return bytes;
    }

    internal static byte[] BlockWithArc(int hostIndex = 0, int familyId = 200, byte active = 1)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(100); writer.Write(0u); writer.Write(0u); writer.Write((ushort)1829);
        foreach (var field in TfBlockDeserializer.SerializationFields(1829)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(100); continue; }
            if (field.Declaring == 37 && field.Name == "m_docAccess") { writer.Write(1u); continue; }
            if (field.Declaring == 1829 && field.Name == "m_familyId") { writer.Write(familyId); continue; }
            if (field.Declaring == 1829 && field.Name == "m_active") { writer.Write(active); continue; }
            if (field.Declaring == 1830 && field.Name == "m_pCutLoops")
            { writer.Write(1); Pointer(writer, 938); continue; }
            if (field.Declaring == 1830 && field.Name == "m_cutDirs")
            { writer.Write(1); writer.Write(0d); writer.Write(-1d); writer.Write(0d); continue; }
            if (field.Declaring == 1830 && field.Name == "m_cutIndices")
            { writer.Write(1); writer.Write(hostIndex); continue; }
            WriteDefault(writer, field);
        }
        // Loop 938: closed, 4 curves (3 GLine 1736, 1 GArc 1974)
        writer.Write((byte)0); writer.Write(4);
        Pointer(writer, 1736);
        Pointer(writer, 1736);
        Pointer(writer, 1974);
        Pointer(writer, 1736);

        // Curve 0: line (-1, 0, 0) -> (1, 0, 0)
        WriteLine(writer, origin: [-1d, 0d, 0d], dir: [2d, 0d, 0d]);
        // Curve 1: line (1, 0, 0) -> (1, 0, 2)
        WriteLine(writer, origin: [1d, 0d, 0d], dir: [0d, 0d, 2d]);
        // Curve 2: arc center (0, 0, 2), radius 1, xVec (1, 0, 0), yVec (0, 0, 1), angle 0 to PI
        WriteArc(writer, center: [0d, 0d, 2d], radius: 1d, xVec: [1d, 0d, 0d], yVec: [0d, 0d, 1d], ep0: 0d, ep1: Math.PI);
        // Curve 3: line (-1, 0, 2) -> (-1, 0, 0)
        WriteLine(writer, origin: [-1d, 0d, 2d], dir: [0d, 0d, -2d]);

        writer.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return bytes;
    }

    private static void WriteLine(BinaryWriter writer, double[] origin, double[] dir)
    {
        writer.Write(0); writer.Write(0); writer.Write(-1); writer.Write(0x080004);
        writer.Write(0d); writer.Write(1d);
        for (int i = 0; i < 3; i++) writer.Write(origin[i]);
        for (int i = 0; i < 3; i++) writer.Write(dir[i]);
    }

    private static void WriteArc(BinaryWriter writer, double[] center, double radius, double[] xVec, double[] yVec, double ep0, double ep1)
    {
        writer.Write(0); writer.Write(0); writer.Write(-1); writer.Write(0x080004);
        writer.Write(ep0); writer.Write(ep1);
        for (int i = 0; i < 3; i++) writer.Write(xVec[i]);
        for (int i = 0; i < 3; i++) writer.Write(yVec[i]);
        writer.Write(radius);
        for (int i = 0; i < 3; i++) writer.Write(center[i]);
        writer.Write((byte)0);
    }

    private static void Pointer(BinaryWriter writer, ushort cls) { writer.Write(uint.MaxValue); writer.Write(cls); }

    private static void WriteDefault(BinaryWriter writer, TfBlockDeserializer.TfField field)
    {
        if (field.Declaring == 297 && field.Name == "m_minmax") { writer.Write(new byte[48]); return; }
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
                foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) WriteDefault(writer, nested); break;
            case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
            default: throw new InvalidOperationException(field.Name);
        }
    }
}

