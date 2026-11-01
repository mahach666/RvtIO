using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class MullionSymbolLengthTests
{
    [Fact]
    public void Decode_CapturesTheOwnedAxisAndIgnoresTheTrimCurve()
    {
        var block = SymbolBlock();
        var parsed = TfBlockDeserializer.ExtractParams(block, 0, BitConverter.ToUInt32(block, 8), 3910)!;
        Assert.True(parsed.TailWalkComplete, string.Join(";", parsed.StringFields.Select(pair => pair.Key + pair.Value)));
        var fields = parsed.DoubleFields;
        Assert.True(fields.ContainsKey("2520:m_pCurve.endParams@0"), string.Join(";", parsed.StringFields.Select(pair => pair.Key + pair.Value)));
        Assert.Equal(2, fields["2520:m_pCurve.endParams@0"]);
        Assert.Equal(12, fields["2520:m_pCurve.endParams@1"]);
        Assert.Equal(-1, fields["2520:m_pCurve.dirVec@2"]);
    }

    [Fact]
    public void Decode_MissingAxisDoesNotCaptureTheTrimCurve()
    {
        var block = SymbolBlock(missingAxis: true);
        var fields = TfBlockDeserializer.ExtractParams(block, 0, BitConverter.ToUInt32(block, 8), 3910)!.DoubleFields;
        Assert.DoesNotContain("2520:m_pCurve.endParams@1", fields.Keys);
    }

    [Fact]
    public void Apply_UsesOwnSymbolAndReplacesApproximateLength()
    {
        var instance = new Element { Id = 10, CategoryId = -2000171, InstanceSymbolId = 20, TypeId = 30 };
        instance.Parameters.Add(new Parameter { Id = -1004005, Value = 3d, StorageType = StorageType.Double, IsApproximate = true });
        var own = Axis();
        var master = Axis(); master.DoubleFields["2520:m_pCurve.endParams@1"] = 100;
        ElementBuilder.ApplyMullionSymbolLength([instance], new Dictionary<uint, TfBlockDeserializer.NativeParams> { [20] = own, [30] = master });
        var length = Assert.Single(instance.Parameters);
        Assert.Equal(10d, length.Value);
        Assert.False(length.IsApproximate);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nonfinite")]
    [InlineData("zero")]
    [InlineData("no-own-symbol")]
    [InlineData("wrong-category")]
    public void Apply_UnsupportedSourcePreservesTheExistingValue(string mutation)
    {
        var instance = new Element { Id = 10, CategoryId = -2000171, InstanceSymbolId = 20, TypeId = 30 };
        instance.Parameters.Add(new Parameter { Id = -1004005, Value = 3d, StorageType = StorageType.Double, IsApproximate = true });
        var own = Axis();
        if (mutation == "missing") own.DoubleFields.Remove("2520:m_pCurve.dirVec@1");
        if (mutation == "nonfinite") own.DoubleFields["2520:m_pCurve.endParams@1"] = double.NaN;
        if (mutation == "zero") own.DoubleFields["2520:m_pCurve.endParams@1"] = 2;
        if (mutation == "no-own-symbol") instance.InstanceSymbolId = null;
        if (mutation == "wrong-category") instance.CategoryId = -2000023;
        ElementBuilder.ApplyMullionSymbolLength([instance], new Dictionary<uint, TfBlockDeserializer.NativeParams> { [20] = own });
        var length = Assert.Single(instance.Parameters);
        Assert.Equal(3d, length.Value);
        Assert.True(length.IsApproximate);
    }

    private static TfBlockDeserializer.NativeParams Axis()
    {
        var result = new TfBlockDeserializer.NativeParams();
        result.DoubleFields["2520:m_pCurve.endParams@0"] = 2;
        result.DoubleFields["2520:m_pCurve.endParams@1"] = 12;
        for (int axis = 0; axis < 3; axis++) result.DoubleFields["2520:m_pCurve.dirVec@" + axis] = axis == 2 ? -1 : 0;
        return result;
    }

    private static byte[] SymbolBlock(bool missingAxis = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(20); writer.Write(0u); writer.Write(0u); writer.Write((ushort)3910);
        foreach (var field in TfBlockDeserializer.SerializationFields(3910)!)
        {
            if (field.Declaring == 1829 && field.Name == "m_refs")
            { writer.Write(1u); Pointer(2520); }
            else Default(field);
        }
        foreach (var field in TfBlockDeserializer.SerializationFields(2520)!)
        {
            if (field.Name == "m_pCurve") { if (missingAxis) writer.Write(0u); else Pointer(1736); }
            else if (field.Name == "m_trimEnd0") Pointer(1736);
            else Default(field);
        }
        if (!missingAxis) Line(2, 12);
        Line(0, 99);
        writer.Write(0u);
        var block = stream.ToArray();
        uint size = (uint)block.Length - 16;
        BitConverter.GetBytes(size).CopyTo(block, 8);
        BitConverter.GetBytes(size).CopyTo(block, block.Length - 4);
        return block;

        void Pointer(ushort cls) { writer.Write(uint.MaxValue); writer.Write(cls); }
        void Line(double start, double end)
        {
            foreach (var field in TfBlockDeserializer.SerializationFields(1736)!)
            {
                if (field.Name == "m_endParams") { writer.Write(start); writer.Write(end); }
                else if (field.Name == "m_dirVec") { writer.Write(0d); writer.Write(0d); writer.Write(-1d); }
                else Default(field);
            }
        }
        void Default(TfBlockDeserializer.TfField field)
        {
            if (field.Declaring == 297 && field.Name == "m_minmax") { writer.Write(new byte[48]); return; }
            if (field.Declaring == 1246 && field.Name == "m_GInfo") { writer.Write(new byte[16]); return; }
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
