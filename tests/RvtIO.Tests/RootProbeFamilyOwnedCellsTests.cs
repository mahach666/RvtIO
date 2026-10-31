using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class RootProbeFamilyOwnedCellsTests
{
    [Theory]
    [InlineData(1740)] [InlineData(90)] [InlineData(1812)] [InlineData(775)] [InlineData(1809)]
    public void NativeOwnedCellsConsumeTheirCompleteDeclaredValues(ushort cls)
    {
        byte[] bytes = Cell(cls);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, cls, 1772);
        Assert.Null(result.Failure); Assert.Equal(bytes.Length, result.End); Assert.Empty(result.Queued);
        Assert.Contains(result.Fields, field => field.NativeLayout != null);
        Assert.Equal(Convert.ToHexString(bytes), string.Concat(result.Fields.Select(field => field.Bytes)));
    }

    [Theory]
    [InlineData(1740)] [InlineData(90)] [InlineData(1812)] [InlineData(775)] [InlineData(1809)]
    public void EveryNativeCellTruncationStopsAtTheFailedFieldWithoutResync(ushort cls)
    {
        byte[] bytes = Cell(cls);
        for (int end = 0; end < bytes.Length; end++)
        {
            var result = DeferredFieldProbe.Read(bytes, 0, end, cls, 1772);
            Assert.NotNull(result.Failure);
            Assert.Equal(result.Failure, result.Fields[^1].Failure);
            Assert.InRange(result.End, 0, end);
            Assert.All(result.Fields, field => Assert.InRange(field.End, field.Start, end));
        }
    }

    [Theory]
    [InlineData(1740)] [InlineData(1812)] [InlineData(775)] [InlineData(1809)]
    public void AliasesAreNotAppliedToInstanceSymbolOrUnrelatedRootScopes(ushort cls)
    {
        byte[] bytes = Cell(cls);
        foreach (ushort root in new ushort[] { 1794, 1829, 1773, 3708, 0 })
        {
            var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, cls, root);
            Assert.All(result.Fields, field => Assert.Null(field.NativeLayout));
            Assert.True(result.Failure != null || result.End != bytes.Length);
        }
    }

    [Theory]
    [InlineData(1794)] [InlineData(1829)] [InlineData(1773)] [InlineData(3708)] [InlineData(0)]
    public void SourceBoundIndexPairSetUsesGeneralCodecOutsideFamilyDiagnosticScopes(ushort root)
    {
        // The global90->91 contract is now corrected in the library; it is no longer one of the unresolved, family-only diagnostic aliases.
        byte[] bytes = Cell(90);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 90, root);
        Assert.Null(result.Failure); Assert.Equal(bytes.Length, result.End); Assert.Empty(result.Queued);
        Assert.All(result.Fields, field => Assert.Null(field.NativeLayout));
        Assert.Equal(Convert.ToHexString(bytes), string.Concat(result.Fields.Select(field => field.Bytes)));
        Assert.Null(TfBlockDeserializer.SerializationFields(90)!.Single().Ref);
    }

    [Theory]
    [InlineData(1740)] [InlineData(90)] [InlineData(1812)] [InlineData(775)] [InlineData(1809)]
    public void AliasReadsStayInsideTheRequestedSubspan(ushort cls)
    {
        byte[] cell = Cell(cls);
        byte[] bytes = [1, 2, 3, .. cell, 4, 5];
        var result = DeferredFieldProbe.Read(bytes, 3, 3 + cell.Length, cls, 1772);
        Assert.Null(result.Failure); Assert.Equal(3 + cell.Length, result.End);
        Assert.Equal(Convert.ToHexString(cell), string.Concat(result.Fields.Select(field => field.Bytes)));
    }

    [Fact]
    public void OwnedRelevantParametersKeepTheirClassAndHandleInTheQueue()
    {
        byte[] bytes = Cell(775, relevant: true);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, 775, 1772);
        Assert.Null(result.Failure); Assert.Equal(bytes.Length, result.End);
        Assert.Equal(((ushort)714, uint.MaxValue), Assert.Single(result.Queued));
        Assert.Equal(6, Assert.Single(result.Fields, field => field.Name == "m_oRelevantParams").End
            - Assert.Single(result.Fields, field => field.Name == "m_oRelevantParams").Start);
    }

    [Theory]
    [InlineData(1740)] [InlineData(90)] [InlineData(1812)] [InlineData(775)] [InlineData(1809)]
    public void UnboundedCountsFailWithoutConsumingAnyRecord(ushort cls)
    {
        byte[] bytes = cls == 1740 ? [.. new byte[16], .. BitConverter.GetBytes(1_000_001u)]
            : BitConverter.GetBytes(1_000_001u);
        var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, cls, 1772);
        Assert.NotNull(result.Failure); Assert.Empty(result.Queued);
        Assert.Equal(bytes.Length, result.End); Assert.Equal(result.Failure, result.Fields[^1].Failure);
    }

    [Theory]
    [InlineData(0u)] [InlineData(uint.MaxValue)]
    public void EmptyAndNullPairContainersDoNotReadARecord(uint count)
    {
        byte[] bytes = BitConverter.GetBytes(count);
        foreach (ushort cls in new ushort[] { 90, 1812, 1809 })
        {
            var result = DeferredFieldProbe.Read(bytes, 0, bytes.Length, cls, 1772);
            Assert.Null(result.Failure); Assert.Equal(4, result.End); Assert.Empty(result.Queued);
        }
        byte[] connector = [.. bytes, .. new byte[20]];
        Assert.Null(DeferredFieldProbe.Read(connector, 0, connector.Length, 775, 1772).Failure);
    }

    [Theory]
    [InlineData(1740)] [InlineData(90)] [InlineData(1812)] [InlineData(775)] [InlineData(1809)]
    public void ReadingNativeCellsNeverMutatesAnySharedDescriptor(ushort cls)
    {
        int[] classes = [1740, 866, 141, 90, 91, 775, 24, 1812, 1813, 1814, 1809, 1810];
        var before = classes.ToDictionary(id => id, id => TfBlockDeserializer.SerializationFields(id)!.ToArray());
        byte[] bytes = Cell(cls);
        Assert.Null(DeferredFieldProbe.Read(bytes, 0, bytes.Length, cls, 1772).Failure);
        foreach (int id in classes) Assert.Equal(before[id], TfBlockDeserializer.SerializationFields(id));
    }

    private static byte[] Cell(ushort cls, bool relevant = false)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        switch (cls)
        {
            case 1740:
                writer.Write(new byte[16]); // own content-document GUID
                writer.Write(2u); writer.Write(17); writer.Write(18); writer.Write(-1); writer.Write(int.MaxValue);
                writer.Write(3u); writer.Write(7); writer.Write(8); writer.Write(9);
                break;
            case 90:
                writer.Write(2u); writer.Write(17); writer.Write(18); writer.Write(-1); writer.Write(int.MaxValue);
                break;
            case 1812:
                writer.Write(2u); writer.Write(17); writer.Write(18); Text("Центр (Front/Back)");
                writer.Write(-1); writer.Write(int.MaxValue); Text("");
                break;
            case 775:
                writer.Write(2u); writer.Write(-1140004); writer.Write(776053);
                writer.Write(-1140002); writer.Write(776054);
                writer.Write(relevant ? uint.MaxValue : 0u);
                if (relevant) writer.Write((ushort)714);
                writer.Write(1); writer.Write(2); writer.Write(1); writer.Write(-1);
                break;
            case 1809:
                writer.Write(2u); writer.Write(3u); writer.Write(-1); writer.Write(17); writer.Write(int.MaxValue);
                writer.Write(-1140001); writer.Write(0u); writer.Write(-1);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(cls));
        }
        return stream.ToArray();

        void Text(string text)
        {
            writer.Write((uint)text.Length);
            foreach (char value in text) writer.Write((ushort)value);
        }
    }
}
