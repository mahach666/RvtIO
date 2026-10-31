using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Ifc;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class IfcCurrentFamilyHostTests
{
    [SkippableTheory]
    [InlineData(363949, false)] [InlineData(363968, false)] [InlineData(314844, true)]
    public void StoredFreeAndCurtainFamiliesKeepTheirNativeHostPermission(int id, bool expected)
    {
        using var stream = TestFiles.OpenBlock($"sample-a-family-{id}.bin");
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        byte[] bytes = memory.ToArray();
        var native = TfBlockDeserializer.ExtractParams(bytes, 0, BitConverter.ToUInt32(bytes, 8), 1772)!;
        Assert.Equal(0, native.Fields["1772:m_refTypeIds@0"]);
        var state = new ParsingState(); state.NativeParams[(uint)id] = native;
        var family = new Element { Id = id };
        var info = new RevitFileInfo(); info.Elements.Add(family);
        ElementBuilder.PostProcess(info, state);
        Assert.Equal(expected, family.FamilyAllowsRawHost);
    }

    [Theory]
    [InlineData(-2000014, 0, 0, 0, false)]
    [InlineData(-2000014, 0, 0, 1, true)]
    [InlineData(-2000014, 1, 0, 0, true)]
    [InlineData(-2000171, 0, 0, 0, true)]
    [InlineData(-2001320, 0, 1, 0, true)]
    [InlineData(-2001320, 0, 0, 0, false)]
    [InlineData(-2000014, 0, 1, 0, false)]
    public void HostPermissionUsesTheNativeFamilyExceptions(int category, int curtain, int curve, int host, bool expected)
    {
        var native = new TfBlockDeserializer.NativeParams();
        native.Fields["1773:m_categoryId"] = category;
        native.Fields["1773:m_isCurtainPanel"] = curtain;
        native.Fields["1773:m_isCrvDriven"] = curve;
        native.Fields["1772:m_refTypeIds@0"] = host;
        var state = new ParsingState(); state.NativeParams[100] = native;
        var family = new Element { Id = 100 };
        var info = new RevitFileInfo(); info.Elements.Add(family);
        ElementBuilder.PostProcess(info, state);
        Assert.Equal(expected, family.FamilyAllowsRawHost);
    }

    [Theory]
    [InlineData(null)] [InlineData("hosted")] [InlineData("function")]
    [InlineData("sketch_plane")] [InlineData("unknown_host_class")]
    [InlineData("unknown_family_class")] [InlineData("unknown_instance_class")]
    [InlineData("unknown_master_class")] [InlineData("missing_master")]
    [InlineData("unresolved_family")]
    public void OnlyAValidatedFreeFamilyHidesAnOrdinaryRawHost(string? mutation)
    {
        var family = new Element { Id = 1, SerializedOwnElementClassId = 1772, FamilyAllowsRawHost = false };
        var symbol = new Element { Id = 2, SerializedOwnElementClassId = 1829, SymbolFamilyId = 1 };
        var wallType = new Element { Id = 3, SerializedBasicWallTypeFunction = 1 };
        var wall = new Element { Id = 4, CategoryId = -2000011, TypeId = 3, SerializedOwnElementClassId = 3524 };
        var instance = new Element { Id = 5, CategoryId = -2000014, TypeId = 2,
            NativeMasterSymbolId = 2, SerializedHostId = 4, SerializedOwnElementClassId = 1794 };
        wall.Parameters.Add(new Parameter { Id = -1002050, Definition = new() { Name = "ELEM_TYPE_PARAM" }, StorageType = StorageType.ElementId, Value = 3 });
        instance.Parameters.Add(new Parameter { Id = -1002050, Definition = new() { Name = "ELEM_TYPE_PARAM" }, StorageType = StorageType.ElementId, Value = 2 });
        var byId = new[] { family, symbol, wallType, wall, instance }.ToDictionary(e => e.Id);
        switch (mutation)
        {
            case "hosted": family.FamilyAllowsRawHost = true; break;
            case "function": symbol.SerializedFamilySymbolFunction = 1; break;
            case "sketch_plane": wall.SerializedOwnElementClassId = 3607; break;
            case "unknown_host_class": wall.SerializedOwnElementClassId = null; break;
            case "unknown_family_class": family.SerializedOwnElementClassId = null; break;
            case "unknown_master_class": symbol.SerializedOwnElementClassId = null; break;
            case "unknown_instance_class": instance.SerializedOwnElementClassId = null; break;
            case "missing_master": instance.NativeMasterSymbolId = null; break;
            case "unresolved_family": symbol.SymbolFamilyId = 999; break;
        }
        Assert.Equal(mutation != null, IfcExporter.PureIsElementExternalCurrent(instance, byId));
        Assert.DoesNotContain("FamilyAllowsRawHost", JsonSerializer.Serialize(family));
    }

    [Theory]
    [InlineData(new int[0], 0)] [InlineData(new[] { 0 }, 0)]
    [InlineData(new[] { 3, 0 }, 3)] [InlineData(new[] { 0, 3 }, 0)]
    public void FamilyHostTypeUsesTheFirstStoredValueIncludingAnEmptyList(int[] values, int expected)
    {
        byte[] bytes = Frame(values);
        var input = TfBlockDeserializer.ExtractParams(bytes, 0, BitConverter.ToUInt32(bytes, 8), 1772)!;
        Assert.Equal(expected, input.Fields["1772:m_refTypeIds@0"]);
        Assert.Equal(-2000014, input.Fields["1773:m_categoryId"]);
        Assert.Equal(0, input.Fields["1773:m_isCurtainPanel"]);
        Assert.Equal(0, input.Fields["1773:m_isCrvDriven"]);
    }

    [Theory]
    [InlineData("null")] [InlineData("large")] [InlineData("truncated")]
    public void NullListReturnsZeroButMalformedListsDoNotPublishAPartialFirstItem(string mutation)
    {
        byte[] bytes = Frame([3], mutation);
        var input = TfBlockDeserializer.ExtractParams(bytes, 0, BitConverter.ToUInt32(bytes, 8), 1772);
        if (mutation == "null") Assert.Equal(0, input!.Fields["1772:m_refTypeIds@0"]);
        else Assert.True(input == null || !input.Fields.ContainsKey("1772:m_refTypeIds@0"));
    }

    private static byte[] Frame(int[] hosts, string? mutation = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(100); writer.Write(0u); writer.Write(0u); writer.Write((ushort)1772);
        foreach (var field in TfBlockDeserializer.SerializationFields(1772)!)
        {
            if (field.Name == "m_id") writer.Write(100);
            else if (field.Name == "m_docAccess") writer.Write(1u);
            else if (field.Declaring == 1773 && field.Name == "m_categoryId") writer.Write(-2000014);
            else if (field.Declaring == 1772 && field.Name == "m_refTypeIds")
            {
                writer.Write(mutation == "null" ? uint.MaxValue : mutation == "large" ? 1_000_001u
                    : mutation == "truncated" ? (uint)hosts.Length + 1 : (uint)hosts.Length);
                if (mutation != "null") foreach (int host in hosts) writer.Write(host);
                if (mutation == "truncated") break;
            }
            else Default(field);
        }
        writer.Write(0u); byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return bytes;

        void Default(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 is 5 or 6) { writer.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int index = 0; index < count; index++) switch (field.Type)
            {
                case 1: case 2: case 3: writer.Write((byte)0); break;
                case 4: case 5: case 6: case 8: writer.Write(0); break;
                case 7: writer.Write(0d); break;
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
