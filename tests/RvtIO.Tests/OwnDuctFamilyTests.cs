using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Lookup;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class OwnDuctFamilyTests
{
    // Original own AbsDuctType frame 2146. Named m_Profile is at 188; its
    // deferred circular class is empty, followed by three routing-manager counts.
    private const string CircularType = "YggAAI9onC3kAAAAbgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAABiCAAA/////////////////////////////////////wAAAP////9BD/////+QCAAAAAAAAGEyVTAqqTM/AAAAAAAAIEAAAAAAAAAgQP//////////////////////////////////////////////////////////////////////////AAAAAAH/////fAADAAAAnA0MAAAAHwQ+BCAAQwQ8BD4EOwRHBDAEPQQ4BE4EAAAAAAAAAAAAAAAA5AAAAA==";

    [Fact]
    public void CompleteOwnProfileSelectsTheCircularClass()
    {
        byte[] block = Convert.FromBase64String(CircularType);
        Assert.True(TfBlockDeserializer.TryExtractSystemTypeRoot(block, 0, Size(block), 110, out int? profile));
        Assert.Equal(124, profile);
        Assert.Equal("По умолчанию", TfBlockDeserializer.ExtractDuctTypeName(block, 0, Size(block), 110));
        Assert.Null(TfBlockDeserializer.ExtractDuctTypeName(block, 0, Size(block), 104));
    }

    [Theory]
    [InlineData(126)] [InlineData(127)]
    public void RectangularAndOvalTypesHaveTheirOwnNameAndNotTheRoundFamily(ushort profileClass)
    {
        byte[] block = Convert.FromBase64String(CircularType);
        BitConverter.GetBytes(profileClass).CopyTo(block, 192);
        Assert.True(TfBlockDeserializer.TryExtractSystemTypeRoot(block, 0, Size(block), 110, out int? profile));
        Assert.Equal(profileClass, profile);
        Assert.Equal("По умолчанию", TfBlockDeserializer.ExtractDuctTypeName(block, 0, Size(block), 110));
        Assert.NotEqual(SystemFamilyKind.RoundDuct, RevitSystemFamilyNames.KindOf(110, profile, -2008000));
    }

    [Theory]
    [InlineData("header")] [InlineData("footer")] [InlineData("class")]
    [InlineData("owner")] [InlineData("document")] [InlineData("null_profile")]
    [InlineData("external_profile")] [InlineData("other_profile")]
    [InlineData("truncated_tail")] [InlineData("extra_tail")]
    [InlineData("routing_count")] [InlineData("broken_symbol_string")]
    public void UnvalidatedFrameProfileOrDeferredGraphCannotSelectTheLabel(string mutation)
    {
        byte[] block = Convert.FromBase64String(CircularType);
        switch (mutation)
        {
            case "header": block[8] ^= 1; break;
            case "footer": block[^1] ^= 1; break;
            case "class": block[12] ^= 1; break;
            case "owner": block[50] ^= 1; break;
            case "document": BitConverter.GetBytes(2).CopyTo(block, 46); break;
            case "null_profile": BitConverter.GetBytes(0).CopyTo(block, 188); break;
            case "external_profile": BitConverter.GetBytes((ushort)0).CopyTo(block, 192); break;
            case "other_profile": BitConverter.GetBytes((ushort)125).CopyTo(block, 192); break;
            case "truncated_tail": block = Reframe(block[..232]); break;
            case "extra_tail": block = Reframe([.. block[..^4], (byte)0]); break;
            case "routing_count": BitConverter.GetBytes(100_001u).CopyTo(block, 228); break;
            case "broken_symbol_string": BitConverter.GetBytes(uint.MaxValue).CopyTo(block, 200); break;
        }
        Assert.False(TfBlockDeserializer.TryExtractSystemTypeRoot(block, 0, Size(block), 110, out int? profile));
        Assert.Null(profile);
        Assert.Null(TfBlockDeserializer.ExtractDuctTypeName(block, 0, Size(block), 110));
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(int.MaxValue, false)] [InlineData(0, true)]
    public void InvalidBoundsRejectWithoutThrowing(int start, bool excessiveSize)
    {
        byte[] block = Convert.FromBase64String(CircularType);
        Assert.False(TfBlockDeserializer.TryExtractSystemTypeRoot(block, start,
            excessiveSize ? uint.MaxValue : Size(block), 110, out _));
        Assert.Null(TfBlockDeserializer.ExtractDuctTypeName(block, start,
            excessiveSize ? uint.MaxValue : Size(block), 110));
    }

    [Theory]
    [InlineData("nul")] [InlineData("surrogate")] [InlineData("whitespace")]
    public void InvalidOwnSymbolTextCannotBecomeATypeName(string mode)
    {
        byte[] block = Convert.FromBase64String(CircularType);
        ushort word = mode == "surrogate" ? (ushort)0xD800 : mode == "whitespace" ? (ushort)' ' : (ushort)0;
        if (mode == "whitespace")
            for (int p = 204; p < 228; p += 2) BitConverter.GetBytes(word).CopyTo(block, p);
        else BitConverter.GetBytes(word).CopyTo(block, 204);
        Assert.Null(TfBlockDeserializer.ExtractDuctTypeName(block, 0, Size(block), 110));
    }

    [Theory]
    [InlineData(110, 124, -2008000, true)]
    [InlineData(110, null, -2008000, false)] [InlineData(110, 125, -2008000, false)]
    [InlineData(110, 124, -2008005, false)] [InlineData(104, 124, -2008000, false)]
    public void ShapeNeutralTypeClassAloneDoesNotConfirmItsFamily(int cls, int? profile, int category, bool supported)
    {
        Assert.Equal(supported ? SystemFamilyKind.RoundDuct : (SystemFamilyKind?)null,
            RevitSystemFamilyNames.KindOf((ushort)cls, profile, category));
    }

    [Theory]
    [InlineData("ok")] [InlineData("duplicate_root")] [InlineData("foreign_root")]
    [InlineData("walker_owner")] [InlineData("duplicate_element")]
    [InlineData("wrong_category")] [InlineData("block_only")] [InlineData("malformed_frame")]
    public void MaterializationRequiresUniqueOwnTypeAndPreservesPublicMetadata(string mode)
    {
        byte[] block = Convert.FromBase64String(CircularType);
        if (mode == "malformed_frame") block[^1] ^= 1;
        var state = new ParsingState();
        uint owner = mode == "walker_owner" ? 999u : 2146u;
        RvtFileReader.RecordSystemTypeRoot(state, block, 0, Size(block), 110, owner);
        if (mode == "duplicate_root") RvtFileReader.RecordSystemTypeRoot(state, block, 0, Size(block), 110, owner);
        if (mode == "foreign_root") RvtFileReader.RecordSystemTypeRoot(state, block, 0, Size(block), 1829, owner);
        var type = new Element { Id = (int)owner, CategoryId = -2008000, Name = "public guess", FamilyName = "public family" };
        if (mode == "wrong_category") type.CategoryId = -2008005;
        if (mode == "block_only") type.IsBlockOnly = true;
        type.ValidatedDuctTypeName = "stale validation";
        var elements = new List<Element> { type };
        if (mode == "duplicate_element") elements.Add(new Element { Id = type.Id });
        string before = JsonSerializer.Serialize(elements);
        ElementBuilder.ApplyValidatedSystemFamilyNames(elements, state, RevitSystemFamilyNames.ForTests(
            new Dictionary<SystemFamilyKind, string> { [SystemFamilyKind.RoundDuct] = "Original localized label" }));
        // Family-resource lookup retains its existing block-only policy; an IFC
        // type name needs the stricter materialized-element validation below.
        Assert.Equal(mode is "ok" or "block_only" ? "Original localized label" : null, type.ValidatedSystemFamilyName);
        ElementBuilder.ApplyValidatedDuctTypeNames(elements, state);
        Assert.Equal(mode == "ok" ? "По умолчанию" : null, type.ValidatedDuctTypeName);
        Assert.Equal(before, JsonSerializer.Serialize(elements));
    }

    [Theory]
    [InlineData("missing_root")] [InlineData("wrong_class")] [InlineData("missing_profile")]
    [InlineData("wrong_profile")] [InlineData("missing_name")] [InlineData("duplicate_name")]
    [InlineData("empty_name")] [InlineData("nonpositive_id")]
    public void PartialMaterializationValidationDoesNotReuseAStaleName(string mode)
    {
        var type = new Element { Id = mode == "nonpositive_id" ? 0 : 2146, CategoryId = -2008000,
            Name = "public name", ValidatedDuctTypeName = "stale validation" };
        var state = new ParsingState();
        state.SystemTypeRoots[2146] = (1, 110, 124);
        state.DuctTypeNames[2146] = (1, "own name");
        switch (mode)
        {
            case "missing_root": state.SystemTypeRoots.Clear(); break;
            case "wrong_class": state.SystemTypeRoots[2146] = (1, 104, 124); break;
            case "missing_profile": state.SystemTypeRoots[2146] = (1, 110, null); break;
            case "wrong_profile": state.SystemTypeRoots[2146] = (1, 110, 125); break;
            case "missing_name": state.DuctTypeNames.Clear(); break;
            case "duplicate_name": state.DuctTypeNames[2146] = (2, "own name"); break;
            case "empty_name": state.DuctTypeNames[2146] = (1, " "); break;
        }
        ElementBuilder.ApplyValidatedDuctTypeNames([type], state);
        Assert.Null(type.ValidatedDuctTypeName);
        Assert.Equal("public name", type.Name);
    }

    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);
    private static byte[] Reframe(byte[] payload)
    {
        uint size = checked((uint)payload.Length - 12);
        byte[] framed = [.. payload, .. BitConverter.GetBytes(size)];
        BitConverter.GetBytes(size).CopyTo(framed, 8);
        return framed;
    }
}
