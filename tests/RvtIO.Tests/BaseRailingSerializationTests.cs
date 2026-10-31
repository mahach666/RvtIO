using System.Buffers.Binary;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class BaseRailingSerializationTests
{
    private const ushort BaseRailingClassId = 485;

    // Exact class-485 root blocks from Test_2022.rvt. The first has no continuous-rail
    // override entries; the second has one serialized tagged-null override slot.
    private const string EmptyOverridesPayload =
        "/////0cLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAA8pQgAxtwHAP///////////////7FTAQD//////////wAAAAEAAAAAAAAASKUIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8D8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADwPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA9pQgA////////////////O6UIAP////+RegcACAAAAAAAAQAAAAAAAAAAAAAAE5zw/wUBAAA=";

    private const string TaggedNullOverridePayload =
        "/////0cLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAxOg4Ar74DAP///////////////7FTAQD//////////wAAAAEAAAAAAAAAvzoOAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8D8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADwPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPA/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAyOg4ANRALAP//////////MDoOAP////+RegcACAAAAAAAAQAAAAAAAAAAAAAAE5zw/wsBAAA=";

    // Exact class-3705 StairsRailingAttr block reached by both occurrence fixtures.
    private const string StairsRailingAttributePayload =
        "/////0cL/////0kLAAAAAP////9ICwAAAAAAAAAAAAAAAP////+NAgEAAACRegcA/////////////////////////////////////wAAAP////9BD////////////////8sB/////x0MOPFNfBPfDEAGAAAAAAAAAAAAAAAEAAAA///////////sKAIAAAgAAAAAAAAAAAAAAAuc8P8AAAAAAAAAABWc8P8KhUKhUCiUPxyc8P8KhUKhUCiUPx2c8P848U18E9/sPx+c8P9MJpPJZDLJPySc8P9A9U/9U//EPyWc8P848U18E98MQCac8P8IAAAACJzw/wAAAAAJnPD/AAAAAAqc8P8CAAAADJzw/wAAAAAanPD/AAAAAB6c8P8CAAAAIJzw/wAAAAAjnPD/AAAAAAEAAAAWnPD/ODAAAAEAAAD/////8AEJAAAAUwBIAF8AMQAxADAAMABtAG0A/////8oB/////9cLCAAAAP////8ICv////8ICv////8ICv////8ICv////8ICv////8ICv////8ICv////8ICgkAAAAAAAAAAAAAAAEAAAD/////yQEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/////wAAAAD/////yQH/////yQH/////yQEAAAAAAAAAAAAAAAALAAAATgBlAHcAIABSAGEAaQBsACgAOAApADzzzrwz7whAAAAAAAAAAACkoQoA84UOAAIAAAALAAAATgBlAHcAIABSAGEAaQBsACgANwApAJT0JD1JTwZAAAAAAAAAAACkoQoA84UOAAMAAAALAAAATgBlAHcAIABSAGEAaQBsACgANgApAOz1er1erwNAAAAAAAAAAACkoQoA84UOAAQAAAALAAAATgBlAHcAIABSAGEAaQBsACgANQApAET30D10DwFAAAAAAAAAAACkoQoA84UOAAUAAAALAAAATgBlAHcAIABSAGEAaQBsACgANAApADjxTXwT3/w/AAAAAAAAAACkoQoA84UOAAYAAAALAAAATgBlAHcAIABSAGEAaQBsACgAMwApAOjz+Xw+n/c/AAAAAAAAAACkoQoA84UOAAcAAAALAAAATgBlAHcAIABSAGEAaQBsACgAMgApAJj2pX1pX/I/AAAAAAAAAACkoQoA84UOAAgAAAALAAAATgBlAHcAIABSAGEAaQBsACgAMQApAJDyo/woP+o/AAAAAAAAAACkoQoA84UOAAkAAAAQAAAAUgBlAGcAdQBsAGEAcgAgAGIAYQBsAHUAcwB0AGUAcgABAAAA7PV6vV6vE0AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD/////ODAAAP7///8ACwAAAEMAbwByAG4AZQByACAAUABvAHMAdAABAAAAAAAAAAAAAAB86B66h+7pvwAAAAAAAAAAAAAAAAAAAAD/////ODAAAP7///8BCAAAAEUAbgBkACAAUABvAHMAdAABAAAAAAAAAAAAAAB86B66h+7pvwAAAAAAAAAAAAAAAAAAAAD/////ODAAAP7///8BCgAAAFMAdABhAHIAdAAgAFAAbwBzAHQAAQAAAAAAAAAAAAAAfOgeuofu6b8AAAAAAAAAAAAAAAAAAAAA/////zgwAAD+////AdoEAAA=";

    [Fact]
    public void ExtractParams_BaseRailingWithNoOverrides_DecodesExactLinks()
    {
        var decoded = Decode(EmptyOverridesPayload, BaseRailingClassId);

        Assert.NotNull(decoded);
        Assert.Equal(566588, decoded.Fields["m_id"]);
        Assert.Equal(566589, decoded.Fields["m_baseRailingSymbolId"]);
        Assert.Equal(490129, decoded.Fields["m_stairsRailingAttrId"]);
        Assert.Contains(decoded.Doubles,
            value => value.ParamId == -1008621 && value.Value == 0.0);
    }

    [Fact]
    public void ExtractParams_BaseRailingWithTaggedNullOverride_DecodesExactLinks()
    {
        var decoded = Decode(TaggedNullOverridePayload, BaseRailingClassId);

        Assert.NotNull(decoded);
        Assert.Equal(932401, decoded.Fields["m_id"]);
        Assert.Equal(932402, decoded.Fields["m_baseRailingSymbolId"]);
        Assert.Equal(490129, decoded.Fields["m_stairsRailingAttrId"]);
        Assert.Contains(decoded.Doubles,
            value => value.ParamId == -1008621 && value.Value == 0.0);
    }

    [Fact]
    public void ExtractParams_BaseRailingTaggedDeferredOverride_PreservesLegacyHeader()
    {
        byte[] payload = Convert.FromBase64String(TaggedNullOverridePayload);
        const int overrideHandlePayloadOffset = 89;
        const int overrideTagPayloadOffset = 93;
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(overrideHandlePayloadOffset, 4), uint.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(overrideTagPayloadOffset, 2), BaseRailingClassId);

        var decoded = Decode(payload, BaseRailingClassId);

        Assert.NotNull(decoded);
        Assert.Equal(932401, decoded.Fields["m_id"]);
        Assert.Equal(932402, decoded.Fields["m_baseRailingSymbolId"]);
        Assert.Equal(490129, decoded.Fields["m_stairsRailingAttrId"]);
    }

    [Fact]
    public void ExtractParams_TruncatedTaggedNullOverride_FailsClosed()
    {
        byte[] source = Convert.FromBase64String(TaggedNullOverridePayload);
        const int endImmediatelyAfterOverrideHandle = 93;
        var truncatedPayload = new byte[endImmediatelyAfterOverrideHandle + 4];
        source.AsSpan(0, endImmediatelyAfterOverrideHandle).CopyTo(truncatedPayload);
        WritePayloadFooter(truncatedPayload);
        byte[] block = BuildBlock(truncatedPayload, BaseRailingClassId);

        Assert.Null(TfBlockDeserializer.ExtractParams(
            block, 0, BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(8, 4)),
            BaseRailingClassId));
    }

    [Fact]
    public void ExtractParams_StairsRailingAttribute_CapturesAuthoredTopHeight()
    {
        var decoded = Decode(StairsRailingAttributePayload, 3705);

        Assert.NotNull(decoded);
        Assert.Equal(490129, decoded.Fields["m_id"]);
        Assert.Equal(3.608923884514436,
            decoded.DoubleFields["m_topRailHeight"]);
    }

    [Fact]
    public void RailingData_TransfersOnlyNamedOccurrenceAndResolvedAttributeFields()
    {
        const int occurrenceId = 932401;
        const int symbolId = 932402;
        const int attributeId = 490129;
        var occurrence = new Element { Id = occurrenceId };
        var occurrenceParams = Decode(
            TaggedNullOverridePayload, BaseRailingClassId)!;
        var attributeParams = Decode(StairsRailingAttributePayload, 3705)!;
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)occurrenceId] = occurrenceParams,
            [(uint)attributeId] = attributeParams,
        };
        var classIds = new Dictionary<uint, ushort>
        {
            [(uint)occurrenceId] = BaseRailingClassId,
            [(uint)attributeId] = 3705,
        };

        ElementBuilder.ApplySerializedRailingData(
            [occurrence], nativeParams, classIds);

        Assert.Equal(symbolId, occurrence.SerializedBaseRailingSymbolId);
        Assert.Equal(attributeId, occurrence.SerializedStairsRailingAttrId);
        Assert.Equal(3.608923884514436,
            occurrence.SerializedRailingTopHeightFeet);
    }

    [Fact]
    public void RailingData_DoesNotSubstituteStoredHeightParameterForNamedField()
    {
        const int occurrenceId = 932401;
        const int attributeId = 490129;
        var occurrence = new Element { Id = occurrenceId };
        var occurrenceParams = Decode(
            TaggedNullOverridePayload, BaseRailingClassId)!;
        var attributeParams = new TfBlockDeserializer.NativeParams();
        attributeParams.Fields["m_id"] = attributeId;
        attributeParams.Doubles.Add((-1008602, 3.608923884514436));
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)occurrenceId] = occurrenceParams,
            [(uint)attributeId] = attributeParams,
        };
        var classIds = new Dictionary<uint, ushort>
        {
            [(uint)occurrenceId] = BaseRailingClassId,
            [(uint)attributeId] = 3705,
        };

        ElementBuilder.ApplySerializedRailingData(
            [occurrence], nativeParams, classIds);

        Assert.Equal(attributeId, occurrence.SerializedStairsRailingAttrId);
        Assert.Null(occurrence.SerializedRailingTopHeightFeet);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("owner")]
    public void RailingData_RejectsOccurrenceWithoutExactClassAndOwner(string mismatch)
    {
        const int occurrenceId = 932401;
        const int attributeId = 490129;
        var occurrence = new Element { Id = occurrenceId };
        var occurrenceParams = Decode(
            TaggedNullOverridePayload, BaseRailingClassId)!;
        if (mismatch == "owner")
            occurrenceParams.Fields["m_id"] = occurrenceId + 1;
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)occurrenceId] = occurrenceParams,
            [(uint)attributeId] = Decode(StairsRailingAttributePayload, 3705)!,
        };
        var classIds = new Dictionary<uint, ushort>
        {
            [(uint)occurrenceId] = mismatch == "class" ? (ushort)1794 : BaseRailingClassId,
            [(uint)attributeId] = 3705,
        };

        ElementBuilder.ApplySerializedRailingData(
            [occurrence], nativeParams, classIds);

        Assert.Null(occurrence.SerializedBaseRailingSymbolId);
        Assert.Null(occurrence.SerializedStairsRailingAttrId);
        Assert.Null(occurrence.SerializedRailingTopHeightFeet);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("owner")]
    [InlineData("nonfinite")]
    public void RailingData_OmitsHeightWithoutExactResolvedAttribute(string mismatch)
    {
        const int occurrenceId = 932401;
        const int symbolId = 932402;
        const int attributeId = 490129;
        var occurrence = new Element { Id = occurrenceId };
        var occurrenceParams = Decode(
            TaggedNullOverridePayload, BaseRailingClassId)!;
        var attributeParams = Decode(StairsRailingAttributePayload, 3705)!;
        if (mismatch == "owner")
            attributeParams.Fields["m_id"] = attributeId + 1;
        else if (mismatch == "nonfinite")
            attributeParams.DoubleFields["m_topRailHeight"] = double.NaN;
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)occurrenceId] = occurrenceParams,
            [(uint)attributeId] = attributeParams,
        };
        var classIds = new Dictionary<uint, ushort>
        {
            [(uint)occurrenceId] = BaseRailingClassId,
            [(uint)attributeId] = mismatch == "class" ? (ushort)1794 : (ushort)3705,
        };

        ElementBuilder.ApplySerializedRailingData(
            [occurrence], nativeParams, classIds);

        Assert.Equal(symbolId, occurrence.SerializedBaseRailingSymbolId);
        Assert.Equal(attributeId, occurrence.SerializedStairsRailingAttrId);
        Assert.Null(occurrence.SerializedRailingTopHeightFeet);
    }

    private static TfBlockDeserializer.NativeParams? Decode(
        string payloadBase64, ushort classId)
        => Decode(Convert.FromBase64String(payloadBase64), classId);

    private static TfBlockDeserializer.NativeParams? Decode(
        byte[] payload, ushort classId)
    {
        byte[] block = BuildBlock(payload, classId);
        uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(8, 4));
        return TfBlockDeserializer.ExtractParams(block, 0, dataSize, classId);
    }

    private static byte[] BuildBlock(string payloadBase64, ushort classId)
        => BuildBlock(Convert.FromBase64String(payloadBase64), classId);

    private static byte[] BuildBlock(byte[] payload, ushort classId)
    {
        uint dataSize = checked((uint)(payload.Length - 2));
        var block = new byte[14 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8, 4), dataSize);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(12, 2), classId);
        payload.CopyTo(block, 14);
        return block;
    }

    private static void WritePayloadFooter(byte[] payload)
    {
        uint dataSize = checked((uint)(payload.Length - 2));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(payload.Length - 4), dataSize);
    }
}
