using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class RootProbeDeferredFieldTests
{
    [Theory]
    [InlineData(1794)] [InlineData(1829)] [InlineData(1772)]
    public void OwnFamilyFrameGuardConfirmsIdentityOnlyNotTheWholeSubtype(ushort cls)
    {
        byte[] bytes = Frame(cls);
        Assert.True(DeferredFieldProbe.HasOwnFamilyFrame(bytes, Size(bytes), cls, 123));
        Assert.NotNull(DeferredFieldProbe.Read(bytes, 14, bytes.Length - 4, cls, cls).Failure);
    }

    [Fact]
    public void FrameGuardRejectsEveryTruncationAndExtraBytes()
    {
        byte[] bytes = Frame(1794);
        for (int end = 0; end < bytes.Length; end++)
            Assert.False(DeferredFieldProbe.HasOwnFamilyFrame(bytes[..end], Size(bytes), 1794, 123));
        Assert.False(DeferredFieldProbe.HasOwnFamilyFrame([.. bytes, 99], Size(bytes), 1794, 123));
    }

    [Theory]
    [InlineData(0)] [InlineData(8)] [InlineData(12)] [InlineData(46)] [InlineData(50)]
    public void ConflictingFrameMetadataOrOwnIdentityRejectsBeforeTracing(int offset)
    {
        byte[] bytes = Frame(1794); uint size = Size(bytes);
        bytes[offset] ^= 1;
        Assert.False(DeferredFieldProbe.HasOwnFamilyFrame(bytes, size, 1794, 123));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(124)]
    public void WrongRequestedOwnerIsNotReplacedWithTheDecodedOwner(int owner)
    {
        byte[] bytes = Frame(1794);
        Assert.False(DeferredFieldProbe.HasOwnFamilyFrame(bytes, Size(bytes), 1794, owner));
    }

    [Fact]
    public void WrongFooterAndNonFamilyClassRejectBeforeTracing()
    {
        byte[] bytes = Frame(1794); bytes[^1] ^= 1;
        Assert.False(DeferredFieldProbe.HasOwnFamilyFrame(bytes, Size(bytes), 1794, 123));
        bytes = Frame(3708);
        Assert.False(DeferredFieldProbe.HasOwnFamilyFrame(bytes, Size(bytes), 3708, 123));
    }

    [Theory]
    [InlineData(-1, -1, 0d)]
    [InlineData(17, 18, 0.125d)]
    [InlineData(0, int.MaxValue, -3.5d)]
    public void LargestMotorValueIsAnInlinePairNotAMapCount(int classification, int motor, double load)
    {
        byte[] bytes = Manager(classification, motor, load);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 1285, 1794);
        Assert.Null(result.Failure); Assert.Equal(bytes.Length, result.End); Assert.Empty(result.Queued);
        var pair = Assert.Single(result.Fields, f => f.Name == "m_LargestMotorMap");
        Assert.Equal(45, pair.Start); Assert.Equal(61, pair.End); Assert.Null(pair.Ref);
        Assert.Equal("1289:first ElementId, second inline 22(ElementId,double)", pair.NativeLayout);
        byte[] value = Convert.FromHexString(pair.Bytes);
        Assert.Equal(classification, BitConverter.ToInt32(value));
        Assert.Equal(motor, BitConverter.ToInt32(value, 4));
        Assert.Equal(BitConverter.DoubleToInt64Bits(load), BitConverter.ToInt64(value, 8));
        Assert.Equal("m_eCircuitNumberingOption", result.Fields[^1].Name);
    }

    [Fact]
    public void EveryManagerTruncationRetainsTheFailedFieldWithoutResync()
    {
        byte[] bytes = Manager(17, 18, 0.125);
        for (int end = 0; end < bytes.Length; end++)
        {
            var result = DeferredFieldProbe.Read(bytes, 0, end, 1285, 1794);
            Assert.NotNull(result.Failure); Assert.NotEmpty(result.Fields);
            Assert.Equal(result.Failure, result.Fields[^1].Failure);
            Assert.InRange(result.End, 0, end);
            Assert.All(result.Fields, field => Assert.InRange(field.End, field.Start, end));
        }
    }

    [Theory]
    [InlineData(1829)] [InlineData(3708)] [InlineData(0)]
    public void NativePairCorrectionDoesNotBroadenOtherRootScopes(ushort rootClass)
    {
        byte[] bytes = Manager(-1, -1, 0);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 1285, rootClass);
        Assert.NotNull(result.Failure);
        Assert.Equal("m_LargestMotorMap", result.Fields[^1].Name);
        Assert.Null(result.Fields[^1].NativeLayout);
        Assert.DoesNotContain(result.Fields, f => f.Name == "m_dDemandLoad");
    }

    [Fact]
    public void PairCorrectionNeverMutatesTheSharedSchema()
    {
        byte[] bytes = Manager(-1, -1, 0);
        var manager = TfBlockDeserializer.SerializationFields(1285)!;
        var pair = TfBlockDeserializer.SerializationFields(1289)!;
        var before = manager.ToArray(); var pairBefore = pair.ToArray();
        Assert.Null(DeferredFieldProbe.Read(bytes, 0, bytes.Length, 1285, 1794).Failure);
        Assert.Equal(before, TfBlockDeserializer.SerializationFields(1285));
        Assert.Equal(pairBefore, TfBlockDeserializer.SerializationFields(1289));
        Assert.Null(Assert.Single(manager, f => f.Name == "m_LargestMotorMap").Ref);
        Assert.Equal(1289, Assert.Single(pair, f => f.Name == "second").Ref);
    }

    [Fact]
    public void SubspanTraceDoesNotReadOrReturnBytesOutsideItsLimit()
    {
        byte[] manager = Manager(-1, -1, 0);
        byte[] bytes = [10, 11, 12, .. manager, 13, 14];
        var result = DeferredFieldProbe.Read(bytes, 3, 3 + manager.Length, 1285, 1794);
        Assert.Null(result.Failure); Assert.Equal(3 + manager.Length, result.End);
        Assert.Equal(Convert.ToHexString(manager), string.Concat(result.Fields.Select(f => f.Bytes)));
    }

    [Fact]
    public void OwnDeferredPointersRetainTheirDeclaredClassAndHandle()
    {
        byte[] bytes = Manager(-1, -1, 0, pressure: true);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 1285, 1794);
        Assert.Null(result.Failure);
        Assert.Equal(((ushort)771, uint.MaxValue), Assert.Single(result.Queued));
        var field = Assert.Single(result.Fields, f => f.Name == "m_pressureDropCalculationServerInfo");
        Assert.Equal(6, field.End - field.Start);
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(0, -1)] [InlineData(2, 1)]
    [InlineData(int.MaxValue, int.MaxValue)] [InlineData(0, int.MaxValue)]
    public void BadBoundsAreRejectedWithoutReadingAField(int start, int end)
    {
        var result = DeferredFieldProbe.Read([], start, end, 1285, 1794);
        Assert.Equal("Invalid deferred bounds", result.Failure);
        Assert.Empty(result.Fields); Assert.Empty(result.Queued);
    }

    [Fact]
    public void MissingSchemaIsNotReportedAsACompleteEmptyObject()
    {
        var result = DeferredFieldProbe.Read([], 0, 0, 65000, 1794);
        Assert.Equal("Missing deferred schema", result.Failure); Assert.Empty(result.Fields);
    }

    [Fact]
    public void FamilyParamsTraceUsesTheExistingNamedRecordGrammar()
    {
        byte[] bytes = new byte[8];
        var result = DeferredFieldProbe.Read(bytes, 0, 8, 714, 1794);
        Assert.Null(result.Failure); Assert.Equal(8, result.End);
        Assert.Equal(new[] { "m_params", "m_geomRefHandles" }, result.Fields.Select(f => f.Name));
        Assert.All(result.Fields, f => Assert.Null(f.NativeLayout));
    }

    [Fact]
    public void UnboundedFamilyParamCountReportsTheExactFailedField()
    {
        byte[] bytes = BitConverter.GetBytes(uint.MaxValue - 1);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 714, 1794);
        Assert.NotNull(result.Failure);
        Assert.Equal("m_params", Assert.Single(result.Fields).Name);
        Assert.Equal(4, result.End); Assert.Empty(result.Queued);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ElectricalParameterMapsReadBothElementIdLeaves(int map)
    {
        byte[] bytes = ParameterMapManager(map, 2);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 1285, 1794);
        Assert.Null(result.Failure); Assert.Equal(bytes.Length, result.End); Assert.Empty(result.Queued);
        var field = Assert.Single(result.Fields, f => f.Name == ParameterMaps[map]);
        Assert.Equal("24:first ElementId, second ElementId", field.NativeLayout);
        byte[] value = Convert.FromHexString(field.Bytes);
        Assert.Equal(20, value.Length); Assert.Equal(2u, BitConverter.ToUInt32(value));
        Assert.Equal(17, BitConverter.ToInt32(value, 4)); Assert.Equal(18, BitConverter.ToInt32(value, 8));
        Assert.Equal(-1, BitConverter.ToInt32(value, 12)); Assert.Equal(int.MaxValue, BitConverter.ToInt32(value, 16));
        Assert.Equal("m_eCircuitNumberingOption", result.Fields[^1].Name);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void EveryNonemptyParameterMapTruncationStopsWithoutResync(int map)
    {
        byte[] bytes = ParameterMapManager(map, 2);
        for (int end = 0; end < bytes.Length; end++)
        {
            var result = DeferredFieldProbe.Read(bytes, 0, end, 1285, 1794);
            Assert.NotNull(result.Failure); Assert.Equal(result.Failure, result.Fields[^1].Failure);
            Assert.InRange(result.End, 0, end);
            Assert.All(result.Fields, field => Assert.InRange(field.End, field.Start, end));
        }
    }

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void EmptyAndNullElectricalParameterMapsReadNoLeaves(uint count)
    {
        for (int map = 0; map < ParameterMaps.Length; map++)
        {
            byte[] bytes = ParameterMapManager(map, count);
            var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 1285, 1794);
            Assert.Null(result.Failure); Assert.Equal(bytes.Length, result.End); Assert.Empty(result.Queued);
            var field = Assert.Single(result.Fields, f => f.Name == ParameterMaps[map]);
            Assert.Equal(4, field.End - field.Start);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void UnboundedElectricalParameterMapStopsAfterItsCount(int map)
    {
        byte[] bytes = ParameterMapManager(map, 100_001u);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 1285, 1794);
        Assert.Equal("Unbounded native pair count", result.Failure); Assert.Empty(result.Queued);
        Assert.Equal(ParameterMaps[map], result.Fields[^1].Name);
        Assert.Equal(4, result.Fields[^1].End - result.Fields[^1].Start);
        Assert.DoesNotContain(result.Fields, f => f.Name == "m_dDemandLoad");
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ElectricalParameterMapSubspanAndSharedSchemaRemainExact(int map)
    {
        int[] classes = [1285, 24, 20];
        var before = classes.ToDictionary(id => id, id => TfBlockDeserializer.SerializationFields(id)!.ToArray());
        byte[] manager = ParameterMapManager(map, 2);
        byte[] bytes = [1, 2, 3, .. manager, 4, 5];
        var result = DeferredFieldProbe.Read(bytes, 3, 3 + manager.Length, 1285, 1794);
        Assert.Null(result.Failure); Assert.Equal(3 + manager.Length, result.End);
        Assert.Equal(Convert.ToHexString(manager), string.Concat(result.Fields.Select(f => f.Bytes)));
        foreach (int cls in classes) Assert.Equal(before[cls], TfBlockDeserializer.SerializationFields(cls));
        foreach (ushort root in new ushort[] { 1772, 1829, 3708, 0 })
        {
            var other = DeferredFieldProbe.Read(manager, 0, manager.Length, 1285, root);
            Assert.NotNull(other.Failure); Assert.All(other.Fields, f => Assert.Null(f.NativeLayout));
        }
    }

    private static readonly string[] ParameterMaps =
    ["m_LCApparentLoadParamMap", "m_LCDemandLoadParamMap", "m_LCDemandFactorParamMap",
     "m_LCApparentCurrentParamMap", "m_LCDemandCurrentParamMap"];

    private static byte[] ParameterMapManager(int map, uint count)
    {
        byte[] empty = Manager(-1, -1, 0);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        int start = 61 + map * 4; // fixture's five count fields, not implementation offsets
        writer.Write(empty.AsSpan(0, start)); writer.Write(count);
        if (count == 2)
        {
            writer.Write(17); writer.Write(18); writer.Write(-1); writer.Write(int.MaxValue);
        }
        writer.Write(empty.AsSpan(start + 4));
        return stream.ToArray();
    }

    private static byte[] Manager(int classification, int motor, double load, bool pressure = false)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(-1); // DesignPropertyManager.m_idCenterLine
        writer.Write(pressure ? uint.MaxValue : 0u);
        if (pressure) writer.Write((ushort)771);
        for (int index = 0; index < 4; index++) writer.Write(-1);
        writer.Write((byte)0);
        for (int index = 0; index < 3; index++) writer.Write(0u); // three own AStrings
        writer.Write(0u); // tap coefficients
        writer.Write(0u); // LC load map
        writer.Write(classification); writer.Write(motor); writer.Write(load);
        for (int index = 0; index < 5; index++) writer.Write(0u); // five parameter maps
        for (int index = 0; index < 4; index++) writer.Write(0d);
        writer.Write(0); // circuit numbering option
        return stream.ToArray();
    }

    private static uint Size(byte[] bytes) => BitConverter.ToUInt32(bytes, 8);

    private static byte[] Frame(ushort cls)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(123u); writer.Write(0u); writer.Write(0u); writer.Write(cls);
        for (int index = 0; index < 6; index++) writer.Write(0u);
        writer.Write(0u); writer.Write(0u); // constraints, cell list
        writer.Write(1u); writer.Write(123); // project document, own Element.m_id
        for (int index = 0; index < 7; index++) writer.Write(-1);
        writer.Write(new byte[3]);
        uint size = checked((uint)stream.Length - 12);
        writer.Write(size); stream.Position = 8; writer.Write(size);
        return stream.ToArray();
    }
}
