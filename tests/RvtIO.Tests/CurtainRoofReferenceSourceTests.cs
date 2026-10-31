using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CurtainRoofReferenceSourceTests
{
    private const string OwnName = "ADSK_Витраж_Макс 1500х1500_Импосты 50х100 мм";

    private static byte[] Read(string name)
    {
        using var stream = TestFiles.OpenBlock($"{name}.bin");
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }

    private static void Record(ParsingState state, byte[] data)
        => RvtFileReader.RecordRoofOrRailingReferenceSource(state, data, 0,
            BitConverter.ToUInt32(data, 8), BitConverter.ToUInt16(data, 12), BitConverter.ToUInt32(data));

    [SkippableFact]
    public void OwnCurtainRoofSymbolInfoFollowsItsSixteenByteMullionArray()
    {
        byte[] data = Read("sample-a-curtain-roof-type-16025");
        Assert.Equal(OwnName, TfBlockDeserializer.ExtractRoofOrRailingTypeName(data, 0,
            BitConverter.ToUInt32(data, 8), 908));
    }

    [SkippableTheory]
    [InlineData(2158349)] [InlineData(2698440)] [InlineData(4066652)] [InlineData(4201316)]
    public void OwnProfileRoofLinkAndSymbolInfoOverrideBorrowedPublicType(int id)
    {
        var state = new ParsingState();
        Record(state, Read($"sample-a-curtain-roof-{id}")); Record(state, Read("sample-a-curtain-roof-type-16025"));
        var roof = new Element { Id = id, CategoryId = -2000035, TypeId = 999, Name = "Public mark" };
        var type = new Element { Id = 16025, CategoryId = -2000035, Name = "Borrowed panel name" };
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([roof, type], state);
        Assert.Equal(OwnName, roof.ValidatedRoofOrRailingTypeReferenceName);
        Assert.Equal(999, roof.TypeId);
        Assert.Equal("Public mark", roof.Name);
        Assert.Equal("Borrowed panel name", type.Name);
    }

    [SkippableTheory]
    [InlineData("duplicate_root")] [InlineData("duplicate_type")] [InlineData("missing_type")]
    [InlineData("wrong_category")] [InlineData("wrong_owner")]
    public void MissingOrAmbiguousSourceClearsStaleValidation(string mutation)
    {
        var state = new ParsingState(); byte[] data = Read("sample-a-curtain-roof-type-16025");
        Record(state, Read("sample-a-curtain-roof-2158349"));
        if (mutation == "wrong_owner") data[0] ^= 1;
        Record(state, data);
        if (mutation == "duplicate_root") Record(state, data);
        var roof = new Element { Id = 2158349, CategoryId = -2000035,
            ValidatedRoofOrRailingTypeReferenceName = "Stale" };
        var type = new Element { Id = 16025, CategoryId = mutation == "wrong_category" ? -2000011 : -2000035 };
        var elements = new List<Element> { roof };
        if (mutation != "missing_type") elements.Add(type);
        if (mutation == "duplicate_type") elements.Add(type);
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames(elements, state);
        Assert.Null(roof.ValidatedRoofOrRailingTypeReferenceName);
    }

    [SkippableTheory]
    [InlineData("footer")] [InlineData("size")] [InlineData("class")]
    [InlineData("owner")] [InlineData("zero_owner")] [InlineData("name_length")]
    public void CorruptOwnRootOrNameFailsClosed(string mutation)
    {
        byte[] data = Read("sample-a-curtain-roof-type-16025");
        switch (mutation)
        {
            case "footer": data[^1] ^= 1; break;
            case "size": data[8] ^= 1; break;
            case "class": data[12] ^= 1; break;
            case "owner": data[0] ^= 1; break;
            case "zero_owner": Array.Clear(data, 0, 4); break;
            // Own SymbolInfo starts at 151 after the complete root, before two LineSpecs.
            case "name_length": Array.Fill(data, (byte)255, 151, 4); break;
        }
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeName(data, 0,
            BitConverter.ToUInt32(data, 8), 908));
    }
}
