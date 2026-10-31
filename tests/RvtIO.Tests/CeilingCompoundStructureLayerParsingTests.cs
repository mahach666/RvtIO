using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public sealed class CeilingCompoundStructureLayerParsingTests
{
    private const ushort CompoundCeilingTypeClassId = 646;
    private const ushort CompoundStructureClassId = 737;
    private const ushort CompoundStructureLayerClassId = 738;
    private const uint TypeElementId = 501;

    [Fact]
    public void NamedCeilingLayers_DecodeFromExactClass646OwnerAndDeferredClass737()
    {
        RawCompoundStructureLayer[] expected =
        [
            Layer(0.125, rawFunction: 4, rawMaterialId: -1, rawLayerId: 7),
            Layer(0.0, rawFunction: 100, rawMaterialId: 0, rawLayerId: 9),
            Layer(0.25, rawFunction: 5, rawMaterialId: 9123, rawLayerId: 11),
        ];
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId, expected);

        Assert.DoesNotContain(Enumerable.Range(0, block.Length - 1), offset =>
            BitConverter.ToUInt16(block, offset) == 0x1003);
        Assert.True(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(100, actual[1].RawFunction);
        Assert.Equal(-1, actual[0].RawMaterialId);
        Assert.Equal(0, actual[1].RawMaterialId);
        Assert.Equal(BitConverter.DoubleToInt64Bits(0.125), actual[0].LayerWidthBits);
        Assert.Equal(7, actual[0].RawLayerId);
        Assert.Equal(11, actual[2].RawLayerId);
    }

    [Fact]
    public void NamedCeilingLayers_MismatchedSerializedOwnerFailsClosed()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId + 1, CompoundStructureClassId,
            [Layer(0.125, 4, 10)]);

        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out _));
    }

    [Fact]
    public void NamedCeilingLayers_OtherRootClassFailsClosed()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)], rootClassId: 645);

        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, 645, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CompoundStructureLayerClassId)]
    public void NamedCeilingLayers_NullOrWrongCompoundPointerFailsClosed(
        ushort? pointerClassId)
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, pointerClassId,
            [Layer(0.125, 4, 10)]);

        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out _));
    }

    [Fact]
    public void NamedCeilingLayers_TruncatedClass737TailFailsClosed()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)], truncateCompoundTail: true);

        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out _));
    }

    [Fact]
    public void NamedCeilingLayers_TruncatedInlineClass738RecordFailsClosed()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)], declaredLayerCount: 2);

        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out _));
    }

    [Fact]
    public void NamedCeilingLayers_NullLayerContainerFailsClosed()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId, [],
            declaredLayerCount: -1);

        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out _));
    }

    [Fact]
    public void NamedCeilingLayers_UnconsumedDeferredTailFailsClosed()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)], appendUnownedTailByte: true);

        Assert.False(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out _));
    }

    [Theory]
    [InlineData(false, false)] // B47, CellList, SymbolInfo, target 737
    [InlineData(true, false)]  // B47, B46, CellList, SymbolInfo, target 737
    [InlineData(true, true)]   // B47, B46, B48, CellList, SymbolInfo, target 737
    public void NamedCeilingLayers_DecodesEveryMeasuredDeferredPrefixShape(
        bool includeStringStore, bool includeElementIdStore)
    {
        RawCompoundStructureLayer expected = Layer(0.375, 5, 77);
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId, [expected],
            includeStringParameterStore: includeStringStore,
            includeElementIdParameterStore: includeElementIdStore,
            includeCellList: true,
            includeSymbolInfo: true);

        Assert.True(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out var actual));
        Assert.Equal(expected, Assert.Single(actual));
    }

    [Fact]
    public void NamedCeilingLayers_UsesNamedPointerQueueIndexNotEarlierClass737()
    {
        RawCompoundStructureLayer unrelated = Layer(0.125, 4, 10);
        RawCompoundStructureLayer target = Layer(0.25, 5, 20);
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId, [target],
            unrelatedSymbolInfoCompoundLayers: [unrelated]);

        Assert.True(TfBlockDeserializer.TryExtractCompoundCeilingLayers(
            block, 0, dataSize, CompoundCeilingTypeClassId, out var actual));
        Assert.Equal(target, Assert.Single(actual));
    }

    [Fact]
    public void CeilingLayerSource_DuplicateExactOwnerBlocksRemainAmbiguous()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)]);
        var state = new ParsingState();

        RvtFileReader.RecordCeilingCompoundStructureLayerSource(
            state, block, 0, dataSize, CompoundCeilingTypeClassId,
            TypeElementId);
        RvtFileReader.RecordCeilingCompoundStructureLayerSource(
            state, block, 0, dataSize, CompoundCeilingTypeClassId,
            TypeElementId);

        var source = state.CeilingCompoundStructureLayerSource[TypeElementId];
        Assert.Equal(2, source.BlockCount);
        Assert.Null(source.Layers);
    }

    [Fact]
    public void CeilingLayerSource_SingleMalformedExactRootIsRecordedFailClosed()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)], truncateCompoundTail: true);
        var state = new ParsingState();

        RvtFileReader.RecordCeilingCompoundStructureLayerSource(
            state, block, 0, dataSize, CompoundCeilingTypeClassId,
            TypeElementId);

        var source = state.CeilingCompoundStructureLayerSource[TypeElementId];
        Assert.Equal(1, source.BlockCount);
        Assert.Null(source.Layers);
    }

    [Fact]
    public void CeilingLayerSource_ValidAndMalformedDuplicateRemainAmbiguous()
    {
        var (valid, validSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)]);
        var (malformed, malformedSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)], truncateCompoundTail: true);
        var state = new ParsingState();

        RvtFileReader.RecordCeilingCompoundStructureLayerSource(
            state, valid, 0, validSize, CompoundCeilingTypeClassId,
            TypeElementId);
        RvtFileReader.RecordCeilingCompoundStructureLayerSource(
            state, malformed, 0, malformedSize, CompoundCeilingTypeClassId,
            TypeElementId);

        var source = state.CeilingCompoundStructureLayerSource[TypeElementId];
        Assert.Equal(2, source.BlockCount);
        Assert.Null(source.Layers);
    }

    [Fact]
    public void CeilingLayerSource_MismatchedWalkerOwnerIsIgnored()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)]);
        var state = new ParsingState();

        RvtFileReader.RecordCeilingCompoundStructureLayerSource(
            state, block, 0, dataSize, CompoundCeilingTypeClassId,
            TypeElementId + 1);

        Assert.Empty(state.CeilingCompoundStructureLayerSource);
    }

    [Fact]
    public void CeilingLayerSource_NonExactFullClassRootIsIgnored()
    {
        var (block, dataSize) = CreateCompoundCeilingTypeBlock(
            TypeElementId, TypeElementId, CompoundStructureClassId,
            [Layer(0.125, 4, 10)], includeFirstParameterStore: false);
        var state = new ParsingState();

        Assert.Equal(0x00000286u, BitConverter.ToUInt32(block, 12));
        RvtFileReader.RecordCeilingCompoundStructureLayerSource(
            state, block, 0, dataSize, CompoundCeilingTypeClassId,
            TypeElementId);

        Assert.Empty(state.CeilingCompoundStructureLayerSource);
    }

    private static RawCompoundStructureLayer Layer(
        double width,
        int rawFunction,
        int rawMaterialId,
        int rawLayerId = 0) => new(
            BitConverter.DoubleToInt64Bits(width),
            rawFunction,
            RawEmbeddingType: -1,
            rawMaterialId,
            RawProfileId: -1,
            rawLayerId,
            RawCapFlag: 1);

    internal static (byte[] Block, uint DataSize) CreateCompoundCeilingTypeBlock(
        uint headerElementId,
        uint serializedElementId,
        ushort? compoundPointerClassId,
        IReadOnlyList<RawCompoundStructureLayer> layers,
        ushort rootClassId = CompoundCeilingTypeClassId,
        bool truncateCompoundTail = false,
        int? declaredLayerCount = null,
        bool includeFirstParameterStore = true,
        bool includeStringParameterStore = false,
        bool includeElementIdParameterStore = false,
        bool includeCellList = false,
        bool includeSymbolInfo = false,
        IReadOnlyList<RawCompoundStructureLayer>?
            unrelatedSymbolInfoCompoundLayers = null,
        bool appendUnownedTailByte = false,
        byte isThick = 0,
        ushort? verticalPointerClassId = null)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(
            payload, System.Text.Encoding.Unicode, leaveOpen: true);

        // Element. A real 0xFFFF0286 type root starts with an owned Double parameter
        // store. Its empty deferred payload precedes the named CompoundStructure.
        if (includeFirstParameterStore)
        {
            writer.Write(uint.MaxValue);
            writer.Write((ushort)0x0B47);
        }
        else
        {
            writer.Write(0u);
        }
        writer.Write(0u); // m_pParamValueSetInt
        WritePointer(writer, includeStringParameterStore ? (ushort)0x0B46 : null);
        WritePointer(writer, includeElementIdParameterStore ? (ushort)0x0B48 : null);
        writer.Write(0u); // m_geomSteps
        writer.Write(0u); // m_pGeomTable
        writer.Write(0u); // m_constrInfo count
        WritePointer(writer, includeCellList ? (ushort)653 : null); // m_cellList
        writer.Write(0u); // m_docAccess
        writer.Write(unchecked((int)serializedElementId)); // m_id
        for (int index = 0; index < 7; index++) writer.Write(0); // ElementId fields
        writer.Write((byte)0); // m_locked
        writer.Write((byte)0); // m_moribund
        writer.Write((byte)0); // m_dummy

        // Symbol + exact HostObjAttr(105).m_pCompoundStructure.
        WritePointer(writer, unrelatedSymbolInfoCompoundLayers != null
            ? CompoundStructureClassId
            : includeSymbolInfo ? (ushort)3905 : null); // m_symbolInfo
        writer.Write(0);  // m_renderStyleId
        writer.Write(0);  // m_previewElemId
        WritePointer(writer, compoundPointerClassId);

        // CompoundCeilingType(646).
        writer.Write(0);       // m_CeilingGridPattern
        writer.Write(isThick); // m_isThick
        writer.Write(0u);      // m_system string length

        if (includeFirstParameterStore)
            writer.Write(0u); // deferred m_pParamValueSetDouble record count

        if (includeStringParameterStore)
            writer.Write(0u); // deferred m_pParamValueSetAString record count
        if (includeElementIdParameterStore)
            writer.Write(0u); // deferred m_pParamValueSetElementId record count
        if (includeCellList)
            writer.Write(0u); // deferred CellList.m_cells count
        if (unrelatedSymbolInfoCompoundLayers != null)
            WriteCompoundStructure(writer, unrelatedSymbolInfoCompoundLayers);
        else if (includeSymbolInfo)
            writer.Write(0u); // deferred SymbolInfo.m_name string length

        if (compoundPointerClassId == CompoundStructureClassId)
        {
            WriteCompoundStructure(
                writer, layers, truncateCompoundTail, declaredLayerCount, verticalPointerClassId);
        }
        if (appendUnownedTailByte)
            writer.Write((byte)0x7F);

        byte[] fields = payload.ToArray();
        uint dataSize = checked((uint)fields.Length + 2);
        byte[] block = new byte[fields.Length + 18];
        BitConverter.GetBytes(headerElementId).CopyTo(block, 0);
        BitConverter.GetBytes(dataSize).CopyTo(block, 8);
        BitConverter.GetBytes(rootClassId).CopyTo(block, 12);
        fields.CopyTo(block, 14);
        BitConverter.GetBytes(dataSize).CopyTo(block, block.Length - 4);
        return (block, dataSize);
    }

    private static void WritePointer(BinaryWriter writer, ushort? classId)
    {
        if (!classId.HasValue)
        {
            writer.Write(0u);
            return;
        }
        writer.Write(uint.MaxValue);
        writer.Write(classId.Value);
    }

    private static void WriteCompoundStructure(
        BinaryWriter writer,
        IReadOnlyList<RawCompoundStructureLayer> layers,
        bool truncateTail = false,
        int? declaredLayerCount = null,
        ushort? verticalPointerClassId = null)
    {
        WritePointer(writer, verticalPointerClassId); // m_oVertRegStructure
        writer.Write(declaredLayerCount ?? layers.Count); // named m_layers
        foreach (RawCompoundStructureLayer layer in layers)
        {
            writer.Write(layer.LayerWidthBits);
            writer.Write(layer.RawFunction);
            writer.Write(layer.RawEmbeddingType);
            writer.Write(layer.RawMaterialId);
            writer.Write(layer.RawProfileId);
            writer.Write(layer.RawLayerId);
            writer.Write(layer.RawCapFlag);
        }

        if (truncateTail) return;
        writer.Write(0);  // m_coarseScaleFillPatternElemId
        writer.Write(0u); // m_coarseScaleFillColor
        for (int index = 0; index < 6; index++) writer.Write(0);
        writer.Write(0u); // m_segRefFaceKeys count
        writer.Write(0u); // m_segRefFaceKeysPre40 count
    }
}
