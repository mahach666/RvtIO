using System.Text;
using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnUnitOptionsTests
{
    private const string Length = "autodesk.spec.aec:length-1.0.0";
    private const string Millimetres = "autodesk.unit.unit:millimeters-1.0.1";
    [Fact]
    public void CompleteOwnRootMatchesDiagnosticWithoutReflection()
    {
        byte[] data = Block(Map([Option(Length)]));
        var units = Read(data); var prior = UnitOptionsProbe.ReadOwnBlock(data, (uint)data.Length - 16, 4029);
        Assert.NotNull(units); Assert.NotNull(prior);
        Assert.Equal((prior.Owner, prior.FamilyId, prior.DocumentAccess, prior.UnitsStart, prior.UnitsEnd),
            (units.OwnerId, units.FamilyId, units.DocumentAccess, units.UnitsStart, units.UnitsEnd));
        Assert.Equal(Millimetres, Assert.Single(units.Value.Options).Unit);
        Assert.Equal(Length, units.Value.Options[0].Spec);
        Assert.Equal((2, 3, 4, 5), (units.Value.UniversalUnitSystem, units.Value.DecimalSymbol,
            units.Value.DigitGroupingSymbol, units.Value.DigitGroupingAmount));
    }
    [Fact]
    public void CompleteMapRetainsAllOptionsWithoutUnitOrDisplayNormalization()
    {
        byte[] data = Block(Map([Option("other", "opaque:other-2.7.3"), Option(Length, Millimetres, "mm", .01, 7, [1, 0, 1, 0, 1, 0])]));
        var units = Read(data); Assert.NotNull(units);
        Assert.Equal(new[] { "other", Length }, units.Value.Options.Select(o => o.Spec));
        Assert.Equal(new TfBlockDeserializer.UnitFormatOption(Length, "mm", Millimetres, .01, 7, 1, 0, 1, 0, 1, 0), units.Value.Options[1]);
    }
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(123, 1)]
    [InlineData(-1, 7)]
    public void FamilyAndDocumentHandlesAreRetainedNotChosen(int family, int document)
    {
        var units = Read(Block(Map([Option(Length)]), family: family, document: document));
        Assert.NotNull(units); Assert.Equal((family, document), (units.FamilyId, units.DocumentAccess));
    }
    [Fact]
    public void AllTruncatedOwnFramesReject()
    {
        byte[] data = Block(Map([Option(Length)]));
        for (int end = 0; end < data.Length; end++) Assert.Null(TfBlockDeserializer.ExtractOwnUnitOptions(data[..end], 0, (uint)data.Length - 16, 4029));
    }
    [Fact]
    public void FrameSubspanHasRelativeOptionPositions()
    {
        byte[] data = Block(Map([Option(Length)])); byte[] padded = [9, 8, .. data, 7, 6];
        var units = TfBlockDeserializer.ExtractOwnUnitOptions(padded, 2, (uint)data.Length - 16, 4029);
        Assert.NotNull(units); Assert.Equal(85, units.UnitsStart); Assert.Equal(Read(data)!.UnitsEnd, units.UnitsEnd);
        Assert.Null(TfBlockDeserializer.ExtractOwnUnitOptions(data, -1, (uint)data.Length - 16, 4029));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(50)]
    public void HeaderAndNamedIdentityMustAgree(int offset)
    { byte[] data = Block(Map([Option(Length)])); data[offset] ^= 1; Assert.Null(Read(data)); }
    [Fact]
    public void NoExtraRootBytesOrWrongFooterOrClass()
    {
        byte[] data = Block(Map([Option(Length)])); byte[] extra = [.. data[..^4], 0, .. data[^4..]]; FixSize(extra); Assert.Null(Read(extra));
        data[^1] ^= 1; Assert.Null(Read(data));
        Assert.Null(TfBlockDeserializer.ExtractOwnUnitOptions(data, 0, (uint)data.Length - 16, 84));
    }
    [Fact]
    public void DuplicateSpecRejectsWholeOwnRoot()
        => Assert.Null(Read(Block(Map([Option(Length), Option(Length, "other")]))));
    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void EmptyCountSentinelsRetainEmptyOptions(uint count)
    {
        byte[] map = Map([]); BitConverter.GetBytes(count).CopyTo(map, 0);
        var units = Read(Block(map)); Assert.NotNull(units); Assert.Empty(units.Value.Options);
    }
    [Theory]
    [InlineData(100001u)]
    [InlineData(uint.MaxValue - 1)]
    public void UnboundedCountsAreRejected(uint count)
    { byte[] map = Map([]); BitConverter.GetBytes(count).CopyTo(map, 0); Assert.Null(Read(Block(map))); }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1d)]
    public void InvalidDisplayAccuracyRejects(double value) => Assert.Null(Read(Block(Map([Option(Length, accuracy: value)]))));
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryBooleanIsCanonical(int index)
    { var flags = new byte[6]; flags[index] = 2; Assert.Null(Read(Block(Map([Option(Length, flags: flags)])))); }
    [Fact]
    public void InvalidUtf16AndHugeLengthsReject()
    {
        byte[] map = Map([Option("x")]); map[8] = 0; map[9] = 0xD8; Assert.Null(Read(Block(map)));
        map = Map([Option(Length)]); BitConverter.GetBytes(10001u).CopyTo(map, 4); Assert.Null(Read(Block(map)));
    }
    [Fact]
    public void UnitMapAllTruncatedPrefixesAreAtomic()
    {
        byte[] map = Map([Option(Length)]);
        for (int end = 0; end < map.Length; end++)
        { Assert.False(TfBlockDeserializer.TryReadUnitOptions(map, 0, end, out int finish, out var units)); Assert.Equal(-1, finish); Assert.Null(units); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateOrMalformedRootCannotBeRepaired(bool malformedFirst)
    {
        var state = new ParsingState(); byte[] data = Block(Map([Option(Length)])); byte[] bad = data.ToArray(); bad[^1] ^= 1;
        Record(state, malformedFirst ? bad : data); Record(state, data);
        Assert.Equal(2, state.OwnUnitOptions[77].BlockCount); Assert.Null(state.OwnUnitOptions[77].Options);
    }
    [Fact]
    public void DifferentOwnersAreNotChosenByNamesOrFamilyFlag()
    {
        var state = new ParsingState(); Record(state, Block(Map([Option(Length)])));
        byte[] foreign = Block(Map([Option(Length, "opaque:feet-1.0.0")]), owner: 88, family: 321); Record(state, foreign, 88);
        Assert.Equal(2, state.OwnUnitOptions.Count); Assert.NotNull(state.OwnUnitOptions[77].Options);
        Assert.Equal(321, state.OwnUnitOptions[88].Options!.FamilyId);
    }
    [Fact]
    public void WrongSuppliedOwnerCountsButDoesNotAcquireSettings()
    { var state = new ParsingState(); Record(state, Block(Map([Option(Length)])), 88); Assert.Null(state.OwnUnitOptions[88].Options); }
    private static TfBlockDeserializer.UnitsElemOptions? Read(byte[] data) => TfBlockDeserializer.ExtractOwnUnitOptions(data, 0, (uint)data.Length - 16, 4029);
    private static void Record(ParsingState state, byte[] data, uint owner = 77) => RvtFileReader.RecordOwnUnitOptions(state, data, 0, (uint)data.Length - 16, 4029, owner);
    private static byte[] Option(string spec, string unit = Millimetres, string symbol = "", double accuracy = 1, int rounding = 0, byte[]? flags = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (string value in new[] { spec, symbol, unit }) { writer.Write((uint)value.Length); writer.Write(Encoding.Unicode.GetBytes(value)); }
        writer.Write(accuracy); writer.Write(rounding); writer.Write(flags ?? new byte[6]); return stream.ToArray();
    }
    private static byte[] Map(byte[][] entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write((uint)entries.Length);
        foreach (byte[] entry in entries) writer.Write(entry); foreach (int value in new[] { 2, 3, 4, 5 }) writer.Write(value); return stream.ToArray();
    }
    private static byte[] Block(byte[] units, uint owner = 77, int family = -1, int document = 1)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(owner); writer.Write(0u); writer.Write(0u); writer.Write((ushort)4029);
        for (int index = 0; index < 8; index++) writer.Write(0u);
        writer.Write(document); writer.Write((int)owner); writer.Write(-1); writer.Write(family);
        for (int index = 0; index < 5; index++) writer.Write(-1); writer.Write(new byte[3]); writer.Write(units); writer.Write(0u);
        byte[] data = stream.ToArray(); FixSize(data); return data;
    }
    private static void FixSize(byte[] data)
    { uint size = (uint)data.Length - 16; BitConverter.GetBytes(size).CopyTo(data, 8); BitConverter.GetBytes(size).CopyTo(data, data.Length - 4); }
}
