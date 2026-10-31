using System.Text;
using Xunit;

namespace RvtIO.Tests;

public class RootProbeUnitOptionsTests
{
    private const string Length = "autodesk.spec.aec:length-1.0.0";
    private const string Millimetres = "autodesk.unit.unit:millimeters-1.0.1";

    [Fact]
    public void CompleteMap_RetainsOrderStringsAccuracyAndEveryFlag()
    {
        byte[] bytes = Map([Option("other"), Option(Length, Millimetres, "mm", .01, 1, [1, 0, 1, 0, 1, 0])]);
        Assert.True(UnitOptionsProbe.TryReadUnits(bytes, 0, bytes.Length, out int end, out var units));
        Assert.Equal(bytes.Length, end);
        Assert.Equal(new[] { "other", Length }, units!.Options.Select(option => option.Spec));
        Assert.Equal(new UnitOptionsProbe.FormatOption(Length, "mm", Millimetres, .01, 1, 1, 0, 1, 0, 1, 0), units.Options[1]);
        Assert.Equal((2, 3, 4, 5), (units.UniversalUnitSystem, units.DecimalSymbol, units.DigitGroupingSymbol, units.DigitGroupingAmount));
    }

    [Fact]
    public void EveryTruncatedPrefix_RejectsAtomically()
    {
        byte[] bytes = Map([Option(Length)]);
        for (int end = 0; end < bytes.Length; end++)
        {
            Assert.False(UnitOptionsProbe.TryReadUnits(bytes, 0, end, out int finish, out var units));
            Assert.Equal(-1, finish); Assert.Null(units);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void EmptySentinels_AreNotInventedLengthSettings(uint count)
    {
        byte[] bytes = Map([]); BitConverter.GetBytes(count).CopyTo(bytes, 0);
        Assert.True(UnitOptionsProbe.TryReadUnits(bytes, 0, bytes.Length, out _, out var units));
        Assert.Empty(units!.Options);
    }

    [Theory]
    [InlineData(100_001u)]
    [InlineData(1_000_000u)]
    public void CountBounds_RejectBeforeAllocation(uint count)
    {
        byte[] bytes = Map([]); BitConverter.GetBytes(count).CopyTo(bytes, 0);
        Assert.False(UnitOptionsProbe.TryReadUnits(bytes, 0, bytes.Length, out _, out _));
    }

    [Fact]
    public void DuplicateSpecs_RejectTheWholeMap()
        => Assert.False(Read(Map([Option(Length), Option(Length, "other")])));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1d)]
    public void InvalidAccuracy_IsNotSilentlyAccepted(double accuracy)
        => Assert.False(Read(Map([Option(Length, accuracy: accuracy)])));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryBoolean_IsValidated(int position)
    {
        byte[] flags = new byte[6]; flags[position] = 2;
        Assert.False(Read(Map([Option(Length, flags: flags)])));
    }

    [Fact]
    public void InvalidUtf16_RejectsInsteadOfReplacingTheNativeId()
    {
        byte[] bytes = Map([Option("x")]); bytes[8] = 0; bytes[9] = 0xD8;
        Assert.False(Read(bytes));
    }

    [Fact]
    public void HugeStringLength_RejectsWithoutReadingOutsideTheSpan()
    {
        byte[] bytes = Map([Option(Length)]); BitConverter.GetBytes(10_001u).CopyTo(bytes, 4);
        Assert.False(Read(bytes));
    }

    [Fact]
    public void SuppliedSubspan_ConsumesOnlyItsOwnMap()
    {
        byte[] map = Map([Option(Length)]);
        byte[] bytes = [9, 8, .. map, 7, 6];
        Assert.True(UnitOptionsProbe.TryReadUnits(bytes, 2, bytes.Length - 2, out int end, out _));
        Assert.Equal(bytes.Length - 2, end);
        Assert.False(UnitOptionsProbe.TryReadUnits(bytes, -1, bytes.Length, out _, out _));
        Assert.False(UnitOptionsProbe.TryReadUnits(bytes, 2, bytes.Length + 1, out _, out _));
    }

    [Fact]
    public void CompleteOwnRoot_RetainsOwnershipWithoutSelectingCurrentDocument()
    {
        byte[] project = Block(Map([Option(Length)]), family: -1);
        var own = UnitOptionsProbe.ReadOwnBlock(project, (uint)project.Length - 16, 4029);
        Assert.NotNull(own); Assert.Equal(77u, own.Owner); Assert.Equal(-1, own.FamilyId);
        Assert.Equal(1, own.DocumentAccess); Assert.Equal(85, own.UnitsStart);
        byte[] family = Block(Map([Option(Length, "feet")]), family: 123);
        var foreign = UnitOptionsProbe.ReadOwnBlock(family, (uint)family.Length - 16, 4029);
        Assert.NotNull(foreign); Assert.Equal(123, foreign.FamilyId);
        Assert.Equal("feet", Assert.Single(foreign.Value.Options).Unit);
    }

    [Fact]
    public void OwnRoot_AllPrefixTruncationsAndTrailingBytesFail()
    {
        byte[] bytes = Block(Map([Option(Length)]));
        for (int size = 0; size < bytes.Length; size++)
            Assert.Null(UnitOptionsProbe.ReadOwnBlock(bytes[..size], (uint)bytes.Length - 16, 4029));
        byte[] extra = [.. bytes[..^4], 0, .. bytes[^4..]];
        uint newSize = (uint)extra.Length - 16;
        BitConverter.GetBytes(newSize).CopyTo(extra, 8); BitConverter.GetBytes(newSize).CopyTo(extra, extra.Length - 4);
        Assert.Null(UnitOptionsProbe.ReadOwnBlock(extra, newSize, 4029));
    }

    [Theory]
    [InlineData(0)] // own owner
    [InlineData(8)] // declared size
    [InlineData(12)] // class tag
    [InlineData(50)] // native m_id
    public void MismatchedOwnFrameOrIdentity_IsRejected(int position)
    {
        byte[] bytes = Block(Map([Option(Length)])); bytes[position] ^= 1;
        Assert.Null(UnitOptionsProbe.ReadOwnBlock(bytes, (uint)bytes.Length - 16, 4029));
    }

    [Fact]
    public void WrongFooterOrForeignClass_IsRejected()
    {
        byte[] bytes = Block(Map([Option(Length)]));
        Assert.Null(UnitOptionsProbe.ReadOwnBlock(bytes, (uint)bytes.Length - 16, 84));
        bytes[^1] ^= 1;
        Assert.Null(UnitOptionsProbe.ReadOwnBlock(bytes, (uint)bytes.Length - 16, 4029));
    }

    private static bool Read(byte[] bytes) => UnitOptionsProbe.TryReadUnits(bytes, 0, bytes.Length, out _, out _);
    private static byte[] Option(string spec, string unit = Millimetres, string symbol = "",
        double accuracy = 1, int rounding = 0, byte[]? flags = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (string value in new[] { spec, symbol, unit })
        { writer.Write((uint)value.Length); writer.Write(Encoding.Unicode.GetBytes(value)); }
        writer.Write(accuracy); writer.Write(rounding); writer.Write(flags ?? new byte[6]);
        return stream.ToArray();
    }
    private static byte[] Map(byte[][] entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((uint)entries.Length); foreach (byte[] entry in entries) writer.Write(entry);
        foreach (int value in new[] { 2, 3, 4, 5 }) writer.Write(value);
        return stream.ToArray();
    }
    private static byte[] Block(byte[] units, int family = -1)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(77u); writer.Write(0u); writer.Write(0u); writer.Write((ushort)4029);
        for (int index = 0; index < 8; index++) writer.Write(0u); // six pointers, constrInfo, cellList
        writer.Write(1u); writer.Write(77); writer.Write(-1); writer.Write(family);
        for (int index = 0; index < 5; index++) writer.Write(-1);
        writer.Write(new byte[3]); writer.Write(units); writer.Write(0u);
        byte[] bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return bytes;
    }
}
