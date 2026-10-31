using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public sealed class NewCurtainWallTypeFunctionTests
{
    private const ushort NewCurtainWallTypeClassId = 2559;
    private const uint TypeElementId = 501;

    [Fact]
    public void NewCurtainWallTypeFunction_DecodesWithoutAnIntParameterStore()
    {
        var (block, dataSize) = CreateWallTypeBlock(
            NewCurtainWallTypeClassId, TypeElementId, TypeElementId, function: 1);

        TfBlockDeserializer.NativeParams? decoded =
            TfBlockDeserializer.ExtractParams(
                block, 0, dataSize, NewCurtainWallTypeClassId);

        Assert.NotNull(decoded);
        Assert.Empty(decoded.Ints);
        Assert.Equal(1, decoded.Fields["m_function"]);
        Assert.True(TfBlockDeserializer.TryExtractNewCurtainWallTypeFunction(
            block, 0, dataSize, NewCurtainWallTypeClassId, out int function));
        Assert.Equal(1, function);
    }

    [Fact]
    public void NewCurtainWallTypeFunction_IsRecordedInStateAndAttachedToItsTypeElement()
    {
        var (block, dataSize) = CreateWallTypeBlock(
            NewCurtainWallTypeClassId, TypeElementId, TypeElementId, function: 1);
        var state = new ParsingState();

        RvtFileReader.RecordNewCurtainWallTypeFunction(
            state, block, 0, dataSize, NewCurtainWallTypeClassId, TypeElementId);

        Assert.Equal((1, (int?)1), state.NewCurtainWallTypeFunctions[TypeElementId]);
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = (int)TypeElementId, TypeId = 513 });
        ElementBuilder.PostProcess(info, state);
        Assert.Equal(1, Assert.Single(info.Elements).SerializedCurtainWallFunction);
    }

    [Fact]
    public void NewCurtainWallTypeFunction_MismatchedSerializedOwnerFailsClosed()
    {
        var (block, dataSize) = CreateWallTypeBlock(
            NewCurtainWallTypeClassId, TypeElementId, TypeElementId + 1, function: 1);

        Assert.False(TfBlockDeserializer.TryExtractNewCurtainWallTypeFunction(
            block, 0, dataSize, NewCurtainWallTypeClassId, out _));
    }

    [Fact]
    public void NewCurtainWallTypeFunction_MismatchedWalkerOwnerFailsClosed()
    {
        var (block, dataSize) = CreateWallTypeBlock(
            NewCurtainWallTypeClassId, TypeElementId, TypeElementId, function: 1);
        var state = new ParsingState();

        RvtFileReader.RecordNewCurtainWallTypeFunction(
            state, block, 0, dataSize, NewCurtainWallTypeClassId, TypeElementId + 1);

        Assert.Equal((1, (int?)null), state.NewCurtainWallTypeFunctions[TypeElementId + 1]);
    }

    [Fact]
    public void NewCurtainWallTypeFunction_MalformedFooterFailsClosed()
    {
        var (block, dataSize) = CreateWallTypeBlock(
            NewCurtainWallTypeClassId, TypeElementId, TypeElementId, function: 1);
        block[^1] ^= 0x01;

        Assert.False(TfBlockDeserializer.TryExtractNewCurtainWallTypeFunction(
            block, 0, dataSize, NewCurtainWallTypeClassId, out _));
    }

    [Theory]
    [InlineData((ushort)914)]  // CurtainWallType
    [InlineData((ushort)2557)] // NRCurtainWallType
    public void NewCurtainWallTypeFunction_OtherWallTypeClassesFailClosed(ushort classId)
    {
        var (block, dataSize) = CreateWallTypeBlock(
            classId, TypeElementId, TypeElementId, function: 1);

        Assert.False(TfBlockDeserializer.TryExtractNewCurtainWallTypeFunction(
            block, 0, dataSize, classId, out _));
        TfBlockDeserializer.NativeParams? decoded =
            TfBlockDeserializer.ExtractParams(block, 0, dataSize, classId);
        Assert.NotNull(decoded);
        Assert.False(decoded.Fields.ContainsKey("m_function"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void NewCurtainWallTypeFunction_OutOfRangeEnumsFailClosed(int function)
    {
        var (block, dataSize) = CreateWallTypeBlock(
            NewCurtainWallTypeClassId, TypeElementId, TypeElementId, function);

        Assert.False(TfBlockDeserializer.TryExtractNewCurtainWallTypeFunction(
            block, 0, dataSize, NewCurtainWallTypeClassId, out _));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void DuplicateOrMixedRoots_ClearFunctionRegardlessOfOrder(bool malformedFirst, bool otherWallClass)
    {
        var (block, size) = CreateWallTypeBlock(NewCurtainWallTypeClassId, TypeElementId, TypeElementId, 1);
        var first = (byte[])block.Clone();
        if (malformedFirst) first[^1] ^= 1;
        var state = new ParsingState();
        RvtFileReader.RecordNewCurtainWallTypeFunction(state, first, 0, size,
            otherWallClass ? (ushort)512 : NewCurtainWallTypeClassId, TypeElementId);
        RvtFileReader.RecordNewCurtainWallTypeFunction(state, block, 0, size, NewCurtainWallTypeClassId, TypeElementId);
        Assert.Equal((2, (int?)null), state.NewCurtainWallTypeFunctions[TypeElementId]);
        // A later valid root cannot repair ambiguity or retain a previously attached value.
        RvtFileReader.RecordNewCurtainWallTypeFunction(state, block, 0, size, NewCurtainWallTypeClassId, TypeElementId);
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = (int)TypeElementId, SerializedCurtainWallFunction = 1 });
        ElementBuilder.PostProcess(info, state);
        Assert.Null(Assert.Single(info.Elements).SerializedCurtainWallFunction);
    }

    [Fact]
    public void OtherWallRootAfterValidCurtainRoot_ClearsFunction()
    {
        var (block, size) = CreateWallTypeBlock(NewCurtainWallTypeClassId, TypeElementId, TypeElementId, 1);
        var state = new ParsingState();
        RvtFileReader.RecordNewCurtainWallTypeFunction(state, block, 0, size, NewCurtainWallTypeClassId, TypeElementId);
        RvtFileReader.RecordNewCurtainWallTypeFunction(state, block, 0, size, 512, TypeElementId);
        Assert.Equal((2, (int?)null), state.NewCurtainWallTypeFunctions[TypeElementId]);
    }

    private static (byte[] Block, uint DataSize) CreateWallTypeBlock(
        ushort classId, uint headerElementId, uint serializedElementId, int function)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(
            payload, System.Text.Encoding.Unicode, leaveOpen: true);

        // Element. All four native parameter stores are deliberately absent; m_function
        // is a WallType schema field and must not depend on m_pParamValueSetInt.
        for (int index = 0; index < 6; index++) writer.Write(0u);
        writer.Write(0u); // m_constrInfo count
        writer.Write(0u); // m_cellList
        writer.Write(0u); // m_docAccess
        writer.Write(unchecked((int)serializedElementId)); // m_id
        for (int index = 0; index < 7; index++) writer.Write(0); // ElementId fields
        writer.Write((byte)0); // m_locked
        writer.Write((byte)0); // m_moribund
        writer.Write((byte)0); // m_dummy

        // Symbol + HostObjAttr.
        writer.Write(0u); // m_symbolInfo
        writer.Write(0);  // m_renderStyleId
        writer.Write(0);  // m_previewElemId
        writer.Write(0u); // m_pCompoundStructure

        // WallType. m_bordMullions is one 16-byte t13 POD with Cnt=2. The old generic
        // path applied Cnt twice and consumed 32 bytes, skipping past m_function.
        writer.Write(0); // m_autoJoinCond
        writer.Write(0); // m_structHiddenViewDisplayType
        writer.Write(-1); // m_panel
        writer.Write(-1); // m_intMullions[0]
        writer.Write(-1); // m_intMullions[1]
        for (int index = 0; index < 4; index++) writer.Write(232770);
        writer.Write(4200); // m_defHeightConstraint
        writer.Write(function); // m_function
        writer.Write((byte)0); // m_fixed
        writer.Write((byte)1); // m_allowAutoEmbed
        writer.Write(0u); // m_oULineSpec
        writer.Write(0u); // m_oVLineSpec

        byte[] fields = payload.ToArray();
        uint dataSize = checked((uint)fields.Length + 2);
        byte[] block = new byte[fields.Length + 18];
        BitConverter.GetBytes(headerElementId).CopyTo(block, 0);
        BitConverter.GetBytes(dataSize).CopyTo(block, 8);
        BitConverter.GetBytes(classId).CopyTo(block, 12);
        fields.CopyTo(block, 14);
        BitConverter.GetBytes(dataSize).CopyTo(block, block.Length - 4);
        return (block, dataSize);
    }
}
