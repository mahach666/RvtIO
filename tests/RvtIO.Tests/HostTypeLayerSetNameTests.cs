using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class HostTypeLayerSetNameTests
{
    [Theory]
    [InlineData(512)] [InlineData(1915)] [InlineData(646)]
    public void CurrentSegmentReferenceRecords_DoNotConsumeTheFollowingContainer(int cls)
    {
        byte[] block = Block((ushort)cls,
            currentRefs: [(3, 29, 0), (4, 30, 1)]);
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), (ushort)cls, out var layers));
        Assert.Equal(new[] { Raw(0.25, 700), Raw(0.125, 701) }, layers);
        if (cls == 646)
            Assert.True(TfBlockDeserializer.TryExtractCompoundCeilingLayers(block, 0, Size(block), (ushort)cls, out _));
    }

    [Theory]
    [InlineData(512)] [InlineData(1915)] [InlineData(646)]
    public void CurrentAndPre40SegmentReferences_AreSeparateOrderedContainers(int cls)
    {
        byte[] block = Block((ushort)cls,
            currentRefs: [(3, 29, 0)], oldRefs: [(4, 30, 0), (16, 31, 1)]);
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), (ushort)cls, out var layers));
        Assert.Equal(2, layers.Length);
    }

    [Theory]
    [InlineData(512)] [InlineData(1915)] [InlineData(646)]
    public void SegmentReferenceIntegerValues_AreNotPolymorphicObjectHandles(int cls)
    {
        byte[] block = Block((ushort)cls,
            currentRefs: [(-1, -1, -1), (4, int.MinValue, int.MaxValue)]);
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), (ushort)cls, out var layers));
        Assert.Equal(2, layers.Length);
    }

    [Theory]
    [InlineData(512)] [InlineData(1915)] [InlineData(646)]
    public void EmptyCurrentReferenceSentinel_PreservesTheLegacyEmptyCase(int cls)
    {
        byte[] block = Block((ushort)cls, "current_refs_sentinel");
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), (ushort)cls, out _));
    }

    [Theory]
    [InlineData("current_refs_truncated")] [InlineData("current_refs_oversized")]
    public void IncompleteSegmentReferenceContainer_FailsClosed(string mode)
    {
        byte[] block = Block(512, mode, currentRefs: [(3, 29, 0)]);
        Assert.False(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), 512, out _));
    }

    [Fact]
    public void RecordedBasicWallType_OwnLayersPrecedeUnimplementedDeferredGeometry()
    {
        // One complete type block. Its later GeomStep/cells are not a source of the CompoundStructure layer fields or SymbolInfo.
        byte[] block = Convert.FromBase64String("fN8IAJq3XAKqAgAAAAL/////RwsAAAAAAAAAAAAAAAD/////JggAAAAAAAAAAP////+NAgEAAAB83wgA/////////////////////////////////////wAAAP////9BD/////+B4A4A/////+ECAAAAAAAAAAD/////////////////////////////////////AQAAAAIAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAA/////wAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAAAAgAAAAIAAAACAAAAAgAAAAIAAAACAAAACQQAAAD/////LwH/////8AH/////ZA//////Zg8bAAAARgBvAHUAbgBkAGEAdABpAG8AbgAgAC0AIAAzADAAMABtAG0AIABDAG8AbgBjAHIAZQB0AGUA/////wMQAQAAAODv9/v9fu8/AQAAAP////8LAgAA/////wAAAAABugEAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////wAAAAACAAAAAAAAAB0AAAAAAAAAAQAAAB4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAA0AAAACAAAAAAAAAAAAAACamZmZmZm5PwEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAP////8AAAAA3u/3+/1+7z8BAAAAAAAAAAAAAAD/////AAAAAAAAAAACAAAAAQAAAAAAAAD/////7PV6vV6vM0ADAAAAAQAAAAAAAAD/////AQAAAAAAAAAEAAAAAAAAAAIAAAABAAAAAwAAAJXWJugLLhE+7PV6vV6vM0Ds9Xq9Xq8zQAAAAAAAAAAAAKoCAAA=");
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), 512, out var layers));
        var layer = Assert.Single(layers);
        Assert.Equal(523, layer.RawMaterialId);
        Assert.Equal(300, Math.Round(BitConverter.Int64BitsToDouble(layer.LayerWidthBits) * 304.8, 2));
        Assert.Equal("Foundation - 300mm Concrete", TfBlockDeserializer.ExtractHostTypeOwnName(block, 0, Size(block), 512));
        block[0] ^= 1;
        Assert.False(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), 512, out _));
    }

    [Theory]
    [InlineData(512)] [InlineData(1915)] [InlineData(646)]
    public void CompleteOwnHostType_ReadsOrderedRawLayersAndOwnSymbolName(int cls)
    {
        byte[] block = Block((ushort)cls);
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), (ushort)cls, out var layers));
        Assert.Equal(new[] { Raw(0.25, 700), Raw(0.125, 701) }, layers);
        Assert.Equal("Own type", TfBlockDeserializer.ExtractHostTypeOwnName(block, 0, Size(block), (ushort)cls));
        if (cls != 646)
            Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(block, 0, Size(block), (ushort)cls, out _));
    }

    [Theory]
    [InlineData("footer")] [InlineData("size")] [InlineData("owner")]
    [InlineData("negative_owner")] [InlineData("class")] [InlineData("tail")]
    [InlineData("missing_pointer")]
    public void InvalidFramingOwnershipOrIncompletePayload_FailsClosed(string mode)
    {
        foreach (ushort cls in new ushort[] { 512, 1915, 646 })
        {
            byte[] block = Block(cls, mode);
            if (mode == "footer") block[^1] ^= 1;
            if (mode == "size") block[8] ^= 1;
            if (mode == "owner") block[0] ^= 1;
            if (mode == "class") block[12] ^= 1;
            Assert.False(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), cls, out _));
        }
    }

    [Theory]
    [InlineData(512)] [InlineData(1915)] [InlineData(646)]
    public void LaterDeferredCells_AreOutsideTheOwnLayerFieldValidation(int cls)
    {
        byte[] block = Block((ushort)cls, "later_cell");
        Assert.True(TfBlockDeserializer.TryExtractHostTypeLayers(block, 0, Size(block), (ushort)cls, out var layers));
        Assert.Equal(new[] { Raw(0.25, 700), Raw(0.125, 701) }, layers);
        Assert.Equal("Own type", TfBlockDeserializer.ExtractHostTypeOwnName(block, 0, Size(block), (ushort)cls));
        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(block, 0, Size(block), (ushort)cls, out _));
        // A broken FIFO item BEFORE the owned structure is never skipped.
        byte[] bad = Block((ushort)cls, "broken_earlier_cell");
        Assert.False(TfBlockDeserializer.TryExtractHostTypeLayers(bad, 0, Size(bad), (ushort)cls, out _));
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("unreadable_first")]
    [InlineData("unsupported_host_root")] [InlineData("walker_owner")]
    public void AllHostTypeRootsCount_AndLaterSuccessCannotRepairValidation(string mode)
    {
        var state = new ParsingState();
        byte[] block = Block(512);
        if (mode == "unreadable_first")
        {
            byte[] bad = (byte[])block.Clone(); bad[^1] ^= 1;
            RvtIO.RvtFileReader.RecordHostTypeCompoundStructure(state, bad, 0, Size(bad), 512, 500);
        }
        RvtIO.RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0, Size(block), 512,
            mode == "walker_owner" ? 501u : 500u);
        if (mode == "duplicate")
            RvtIO.RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0, Size(block), 512, 500);
        if (mode == "unsupported_host_root")
            RvtIO.RvtFileReader.RecordHostTypeCompoundStructure(state, block, 0, Size(block), 105, 500);
        var validation = state.HostTypeCompoundStructures[mode == "walker_owner" ? 501u : 500u];
        Assert.Null(validation.Layers);
        Assert.Null(validation.TypeName);
    }

    [Theory]
    [InlineData(512, -2000011, null)] [InlineData(1915, -2000032, 0)]
    [InlineData(1915, -2001300, 1)] [InlineData(646, -2000038, null)]
    public void Builder_PreservesPublicLayersAndNames_AndClearsStaleValidation(int cls, int category, int? discriminator)
    {
        var state = Source((ushort)cls, discriminator);
        var type = Type((ushort)cls, category);
        type.Name = "Borrowed level";
        string before = JsonSerializer.Serialize(type);
        Assert.Equal(1, ElementBuilder.ApplyHostTypeCompoundStructures([type], state));
        Assert.Equal("Native family:Own type", type.ValidatedHostTypeLayerSetName);
        Assert.NotNull(type.SerializedHostTypeLayers);
        Assert.Equal(before, JsonSerializer.Serialize(type));
        Assert.Equal(0, ElementBuilder.ApplyHostTypeCompoundStructures([type], new ParsingState()));
        Assert.Null(type.SerializedHostTypeLayers);
        Assert.Null(type.ValidatedHostTypeLayerSetName);
    }

    [Theory]
    [InlineData("duplicate_element")] [InlineData("duplicate_root")]
    [InlineData("duplicate_system_root")] [InlineData("wrong_root_class")]
    [InlineData("wrong_category")] [InlineData("block_only")]
    public void Builder_RejectsAmbiguityAndWrongCategory(string mode)
    {
        var state = Source(512, null);
        var type = Type(512, -2000011);
        var elements = new List<Element> { type };
        switch (mode)
        {
            case "duplicate_element": elements.Add(new Element { Id = 500 }); break;
            case "duplicate_root": state.HostTypeCompoundStructures[500] = (2, 512, null, null); break;
            case "duplicate_system_root": state.SystemTypeRoots[500] = (2, 512, null); break;
            case "wrong_root_class": state.SystemTypeRoots[500] = (1, 1915, 0); break;
            case "wrong_category": type.CategoryId = -2000032; break;
            case "block_only": type.IsBlockOnly = true; break;
        }
        Assert.Equal(0, ElementBuilder.ApplyHostTypeCompoundStructures(elements, state));
        Assert.Null(type.ValidatedHostTypeLayerSetName);
    }

    [Theory]
    [InlineData(512, -2000011, "IFCWALL")]
    [InlineData(512, -2000011, "IFCWALLSTANDARDCASE")]
    [InlineData(1915, -2000032, "IFCSLAB")]
    [InlineData(1915, -2001300, "IFCSLAB")]
    [InlineData(646, -2000038, "IFCCOVERING")]
    public void HomogeneousExactLayerGroup_UsesOwnTypeNameOnly(int cls, int category, string ifcType)
    {
        var type = Type((ushort)cls, category);
        Element first = Product(100, category), second = Product(101, category);
        var counts = new Dictionary<int, int> { [100] = 1, [101] = 1, [500] = 1 };
        var elements = new Dictionary<int, Element> { [500] = type };
        string before = JsonSerializer.Serialize(new[] { first, second, type });
        Assert.Equal("Native family:Own type", IfcExporter.ResolveValidatedLayerSetName(
            [(first, ifcType), (second, ifcType)], elements, counts));
        Assert.Equal(before, JsonSerializer.Serialize(new[] { first, second, type }));
    }

    [Fact]
    public void NativeLayers_MatchTheExistingPublicProjection_NotAnEpsilonOrThicknessRewrite()
    {
        var type = Type(512, -2000011);
        var product = Product(100, -2000011);
        type.SerializedHostTypeLayers![0] = Raw(0.123456789, 700);
        product.Layers![0].ThicknessMm = Math.Round(0.123456789 * 304.8, 2);
        double before = product.Layers[0].ThicknessMm;
        var elements = new Dictionary<int, Element> { [500] = type };
        var counts = new Dictionary<int, int> { [100] = 1, [500] = 1 };
        Assert.Equal("Native family:Own type", IfcExporter.ResolveValidatedLayerSetName([(product, "IFCWALL")], elements, counts));
        Assert.Equal(before, product.Layers[0].ThicknessMm);
        product.Layers[0].ThicknessMm = Math.BitIncrement(before);
        Assert.Null(IfcExporter.ResolveValidatedLayerSetName([(product, "IFCWALL")], elements, counts));
        type.SerializedHostTypeLayers[0] = Raw(double.MaxValue, 700);
        product.Layers[0].ThicknessMm = double.PositiveInfinity;
        Assert.Null(IfcExporter.ResolveValidatedLayerSetName([(product, "IFCWALL")], elements, counts));
    }

    [Theory]
    [InlineData("different_type")] [InlineData("missing_link")] [InlineData("missing_type")]
    [InlineData("duplicate_occurrence")] [InlineData("duplicate_type")] [InlineData("wrong_category")]
    [InlineData("wrong_ifc_class")] [InlineData("wrong_type_class")] [InlineData("missing_name")]
    [InlineData("missing_native_layers")] [InlineData("missing_public_layers")] [InlineData("extra_layer")]
    [InlineData("material")] [InlineData("width")] [InlineData("indices")] [InlineData("invalid_native_width")]
    [InlineData("missing_material")] [InlineData("override")]
    public void AnyUnvalidatedGroupMember_BlocksName_InEitherOrder(string mode)
    {
        var type = Type(512, -2000011);
        Element first = Product(100, -2000011), second = Product(101, -2000011);
        var elements = new Dictionary<int, Element> { [500] = type, [501] = Type(512, -2000011) };
        elements[501].Id = 501;
        var counts = new Dictionary<int, int> { [100] = 1, [101] = 1, [500] = 1, [501] = 1 };
        string cls = "IFCWALL";
        switch (mode)
        {
            case "different_type": second.SerializedSystemTypeId = 501; break;
            case "missing_link": second.SerializedSystemTypeId = null; break;
            case "missing_type": elements.Remove(500); break;
            case "duplicate_occurrence": counts[101] = 2; break;
            case "duplicate_type": counts[500] = 2; break;
            case "wrong_category": second.CategoryId = -2000032; break;
            case "wrong_ifc_class": cls = "IFCBUILDINGELEMENTPROXY"; break;
            case "wrong_type_class": type.SerializedSystemTypeClassId = 646; break;
            case "missing_name": type.ValidatedHostTypeLayerSetName = null; break;
            case "missing_native_layers": type.SerializedHostTypeLayers = null; break;
            case "missing_public_layers": second.Layers = null; break;
            case "extra_layer": second.Layers!.Add(new MaterialLayer()); break;
            case "material": second.Layers![0].MaterialId++; break;
            case "width": second.Layers![0].ThicknessMm = Math.BitIncrement(second.Layers[0].ThicknessMm); break;
            case "indices": second.Layers![1].LayerIndex = 0; break;
            case "invalid_native_width": type.SerializedHostTypeLayers![0] = Raw(double.NaN, 700); break;
            case "missing_material": type.SerializedHostTypeLayers![0] = Raw(0.25, -1); break;
            case "override": type.Parameters.Add(new Parameter { Definition = new Definition { Name = "IfcMaterialLayerSet.Name" } }); break;
        }
        foreach (var group in new[] { new[] { (first, "IFCWALL"), (second, cls) }, new[] { (second, cls), (first, "IFCWALL") } })
            Assert.Null(IfcExporter.ResolveValidatedLayerSetName(group, elements, counts));
    }

    private static Element Type(ushort cls, int category) => new() { Id = 500, CategoryId = category,
        ValidatedSystemFamilyName = "Native family", SerializedSystemTypeClassId = cls,
        ValidatedHostTypeLayerSetName = "Native family:Own type", SerializedHostTypeLayers = [Raw(0.25, 700), Raw(0.125, 701)] };

    private static Element Product(int id, int category) => new() { Id = id, CategoryId = category,
        SerializedSystemTypeId = 500, Name = "Legacy occurrence", Layers = [
            new MaterialLayer { LayerIndex = 0, MaterialId = 700, ThicknessMm = 0.25 * 304.8 },
            new MaterialLayer { LayerIndex = 1, MaterialId = 701, ThicknessMm = 0.125 * 304.8 }] };

    private static ParsingState Source(ushort cls, int? discriminator)
    {
        var state = new ParsingState();
        state.SystemTypeRoots[500] = (1, cls, discriminator);
        state.HostTypeCompoundStructures[500] = (1, cls, [Raw(0.25, 700), Raw(0.125, 701)], "Own type");
        return state;
    }

    private static RawCompoundStructureLayer Raw(double feet, int material) => new(
        BitConverter.DoubleToInt64Bits(feet), 1, -1, material, -1, 0, 1);

    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);

    internal static byte[] Block(ushort cls, string? mode = null,
        (int Id, int Key0, int Key1)[]? currentRefs = null,
        (int Id, int Key0, int Key1)[]? oldRefs = null, byte[]? vertical = null, byte isThick = 0,
        RawCompoundStructureLayer[]? layers = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(500u); writer.Write(0u); writer.Write(0u); writer.Write(cls);
        foreach (var field in TfBlockDeserializer.SerializationFields(cls)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id")
                writer.Write(mode == "negative_owner" ? -1 : 500);
            else if (field.Declaring == 37 && field.Name == "m_cellList" && mode is "later_cell" or "broken_earlier_cell")
                Pointer(653);
            else if (field.Declaring == 106 && field.Name == "m_symbolInfo")
                Pointer(3905);
            else if (field.Declaring == 105 && field.Name == "m_pCompoundStructure")
                Pointer(mode == "missing_pointer" ? null : (ushort)737);
            else if (field.Declaring == 646 && field.Name == "m_isThick")
                writer.Write(isThick);
            else Default(field);
        }
        if (mode is "later_cell" or "broken_earlier_cell")
        {
            writer.Write(mode == "broken_earlier_cell" ? uint.MaxValue : 1u);
            Pointer(496); // PatternHelper is later than SymbolInfo/CompoundStructure in the FIFO.
        }
        writer.Write(8u); writer.Write(System.Text.Encoding.Unicode.GetBytes("Own type"));
        if (mode != "missing_pointer")
        {
            Pointer(vertical == null ? null : mode == "wrong_vertical_class" ? (ushort)652 : (ushort)4099);
            layers ??= [Raw(0.25, 700), Raw(0.125, 701) with { RawLayerId = vertical == null ? 0 : 1 }];
            writer.Write((uint)layers.Length);
            foreach (var layer in layers)
            {
                writer.Write(layer.LayerWidthBits); writer.Write(layer.RawFunction); writer.Write(layer.RawEmbeddingType);
                writer.Write(layer.RawMaterialId); writer.Write(layer.RawProfileId); writer.Write(layer.RawLayerId); writer.Write(layer.RawCapFlag);
            }
            writer.Write(0); writer.Write(0u); for (int i = 0; i < 6; i++) writer.Write(0);
            writer.Write(mode == "current_refs_sentinel" ? uint.MaxValue
                : mode == "current_refs_truncated" ? 2u
                : mode == "current_refs_oversized" ? 100001u
                : (uint)(currentRefs?.Length ?? 0));
            foreach (var record in currentRefs ?? [])
            { writer.Write(record.Id); writer.Write(record.Key0); writer.Write(record.Key1); }
            writer.Write((uint)(oldRefs?.Length ?? 0));
            foreach (var record in oldRefs ?? [])
            { writer.Write(record.Id); writer.Write(record.Key0); writer.Write(record.Key1); }
        }
        if (vertical != null) writer.Write(vertical);
        if (mode is "tail" or "invalid_later_vertical_cell") writer.Write((byte)1);
        if (mode == "later_cell") writer.Write((byte)1); // unimplemented later PatternHelper body
        writer.Write(0u); byte[] result = stream.ToArray(); uint size = (uint)result.Length - 16;
        BitConverter.GetBytes(size).CopyTo(result, 8); BitConverter.GetBytes(size).CopyTo(result, result.Length - 4);
        return result;
        void Pointer(ushort? value) { if (!value.HasValue) writer.Write(0u); else { writer.Write(uint.MaxValue); writer.Write(value.Value); } }
        void Default(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 == 5) { writer.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int i = 0; i < count; i++) switch (field.Type)
            {
                case 1: case 2: writer.Write((byte)0); break;
                case 4: writer.Write(0); break;
                case 7: writer.Write(0d); break;
                case 8: writer.Write(0u); break;
                case 13 when field.Declaring == 513 && field.Name == "m_bordMullions": writer.Write(0L); break;
                case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                    foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) Default(nested); break;
                case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(field.Name);
            }
        }
    }
}
