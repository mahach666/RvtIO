using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class StairNameParameterTests
{
    [Theory]
    [InlineData(3681)] [InlineData(3708)] [InlineData(3686)]
    public void AllFourOwnStoresRetainExactTypesValuesAndIds(int cls)
    {
        var fixture = Build((ushort)cls, stores: true);
        var input = Decode(fixture.Bytes, (ushort)cls);
        Assert.NotNull(input); Assert.Equal(42, input.OwnerId);
        Assert.True(input.Values.RootParameterStoresComplete);
        Assert.Equal((101, 12.5), Assert.Single(input.Values.Doubles));
        Assert.Equal((102, 19), Assert.Single(input.Values.Ints));
        Assert.Equal((103, "literal"), Assert.Single(input.Values.Strings));
        Assert.Equal((104, 77), Assert.Single(input.Values.ElementIds));
    }

    [Fact]
    public void SupportNonNullStoresRemainUnvalidatedWithoutTheirFullRootGrammar()
        => Assert.Null(Decode(Build(3727, stores: true).Bytes, 3727));

    [Fact]
    public void SupportNullStoreValidationDoesNotClaimOrReadItsLaterInlineHostReference()
    {
        var fixture = Build(3727);
        int end = fixture.Positions["root/m_oSupportPathCurveLoop"];
        Array.Fill(fixture.Bytes, (byte)0xAB, end, fixture.Bytes.Length - end - 4);
        Assert.NotNull(Decode(fixture.Bytes, 3727));
    }

    [Theory]
    [InlineData(3681)] [InlineData(3708)] [InlineData(3686)] [InlineData(3727)]
    public void ExplicitNullStoresConfirmAbsenceWithoutUsingLaterGeometry(int cls)
    {
        var input = Decode(Build((ushort)cls).Bytes, (ushort)cls);
        Assert.NotNull(input); Assert.True(input.Values.RootParameterStoresComplete);
        Assert.Empty(input.Values.Doubles); Assert.Empty(input.Values.Ints);
        Assert.Empty(input.Values.Strings); Assert.Empty(input.Values.ElementIds);
    }

    [Theory]
    [InlineData(3708,"header")] [InlineData(3708,"footer")] [InlineData(3708,"owner")]
    [InlineData(3708,"document")] [InlineData(3708,"named_id")] [InlineData(3708,"class")]
    [InlineData(3708,"external")] [InlineData(3708,"wrong_store")]
    [InlineData(3708,"count")] [InlineData(3708,"string_length")] [InlineData(3708,"embedded_nul")]
    [InlineData(3727,"header")] [InlineData(3727,"footer")] [InlineData(3727,"owner")]
    [InlineData(3727,"document")] [InlineData(3727,"named_id")] [InlineData(3727,"class")]
    [InlineData(3727,"external")] [InlineData(3727,"wrong_store")]
    [InlineData(3727,"count")] [InlineData(3727,"string_length")] [InlineData(3727,"embedded_nul")]
    public void InvalidOwnParameterAuthorityFailsClosed(int cls, string mode)
    {
        var fixture = Build((ushort)cls, stores: cls != 3727 || mode is "count" or "string_length" or "embedded_nul");
        var b = fixture.Bytes; var p = fixture.Positions;
        void U32(string field, uint value) => BitConverter.GetBytes(value).CopyTo(b, p[field]);
        switch (mode)
        {
            case "header": b[8] ^= 1; break;
            case "footer": b[^1] ^= 1; break;
            case "owner": b[0] ^= 1; break;
            case "document": U32("root/m_docAccess", 2); break;
            case "named_id": U32("root/m_id", 43); break;
            case "class": b[12] ^= 1; break;
            case "external":
                U32("root/m_pParamValueSetDouble", uint.MaxValue);
                BitConverter.GetBytes((ushort)0).CopyTo(b, p["root/m_pParamValueSetDouble"] + 4); break;
            case "wrong_store":
                U32("root/m_pParamValueSetDouble", uint.MaxValue);
                BitConverter.GetBytes((ushort)2889).CopyTo(b, p["root/m_pParamValueSetDouble"] + 4); break;
            case "count": U32("2887/count", 100001); break;
            case "string_length": U32("2886/length", uint.MaxValue - 1); break;
            case "embedded_nul": b[p["2886/length"] + 4] = 0; b[p["2886/length"] + 5] = 0; break;
        }
        Assert.Null(Decode(b, (ushort)cls));
    }

    [Theory]
    [InlineData(3708)] [InlineData(3727)]
    public void EveryOwnParameterPrefixTruncationIsRejected(int cls)
    {
        var fixture = Build((ushort)cls, stores: cls != 3727);
        int end = fixture.Positions[cls == 3727 ? "root/m_oSupportPathCurveLoop" : "end"];
        for (int length = 14; length < end; length++)
        {
            var shortened = fixture.Bytes[..length].Concat(new byte[4]).ToArray(); Frame(shortened);
            Assert.Null(Decode(shortened, (ushort)cls));
        }
    }

    [Theory]
    [InlineData(-1)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void InvalidOffsetsFailClosed(int offset)
        => Assert.Null(TfBlockDeserializer.ExtractStairNameParameters(Build(3708).Bytes, offset, 20, 3708));

    [Fact]
    public void FullOwnDefinitionUsesCaptionAndReciprocalDefinitionOwner()
        => Assert.Equal("IfcName", Definition(Build(2873).Bytes));

    [Theory]
    [InlineData("header")] [InlineData("footer")] [InlineData("owner")]
    [InlineData("document")] [InlineData("named_id")] [InlineData("class")]
    [InlineData("null_definition")] [InlineData("external_definition")]
    [InlineData("wrong_definition")] [InlineData("reciprocal_owner")]
    [InlineData("string_length")] [InlineData("extra")]
    public void DefinitionCannotComeFromForeignPointerOrPartialGraph(string mode)
    {
        var fixture = Build(2873); var b = fixture.Bytes; var p = fixture.Positions;
        void U32(string field, uint value) => BitConverter.GetBytes(value).CopyTo(b, p[field]);
        switch (mode)
        {
            case "header": b[8] ^= 1; break;
            case "footer": b[^1] ^= 1; break;
            case "owner": b[0] ^= 1; break;
            case "document": U32("root/m_docAccess", 2); break;
            case "named_id": U32("root/m_id", 43); break;
            case "class": b[12] ^= 1; break;
            case "null_definition": U32("root/m_pParamDef", 0); break;
            case "external_definition": BitConverter.GetBytes((ushort)0).CopyTo(b, p["root/m_pParamDef"] + 4); break;
            case "wrong_definition": BitConverter.GetBytes((ushort)605).CopyTo(b, p["root/m_pParamDef"] + 4); break;
            case "reciprocal_owner": U32("606/m_paramElemId", 43); break;
            case "string_length": U32("606/m_caption", uint.MaxValue - 1); break;
            case "extra": b = b[..^4].Concat(new byte[5]).ToArray(); Frame(b); break;
        }
        Assert.Null(Definition(b));
    }

    [Fact]
    public void EveryDefinitionPayloadTruncationIsRejected()
    {
        var original = Build(2873).Bytes;
        for (int length = 14; length < original.Length - 4; length++)
        {
            var shortened = original[..length].Concat(new byte[4]).ToArray(); Frame(shortened);
            Assert.Null(Definition(shortened));
        }
    }

    [Theory]
    [InlineData("&IfcName")] [InlineData("\u2026IfcName")] [InlineData("")] [InlineData("IfcName\0foreign")]
    public void UnsupportedCaptionFormsDoNotEstablishAName(string name)
        => Assert.Null(Definition(Build(2873, caption: name).Bytes));

    [Fact]
    public void NamedScopeIsSeparateFromPublicInheritedParameters()
    {
        var (element, state) = Scope();
        element.Parameters.Add(new() { Id = 7, Value = "inherited", Definition = new() { Name = "IfcName" } });
        ElementBuilder.ApplyStairNameParameterScopes([element], state);
        var parameter = Assert.Single(element.SerializedStairNameParameters!);
        Assert.Equal("IfcName", parameter.Definition!.Name); Assert.Equal("own", parameter.Value);
        Assert.Equal(StorageType.String, parameter.StorageType);
        Assert.Equal("inherited", Assert.Single(element.Parameters).Value);
    }

    [Theory]
    [InlineData("no_root")] [InlineData("root_duplicate")] [InlineData("null_input")]
    [InlineData("owner_mismatch")] [InlineData("incomplete")] [InlineData("no_definition")]
    [InlineData("definition_duplicate")] [InlineData("definition_null")]
    [InlineData("duplicate_id")] [InlineData("cross_store_duplicate")]
    [InlineData("zero_id")] [InlineData("unknown_bip")]
    [InlineData("element_duplicate")] [InlineData("block_only")] [InlineData("wrong_class")]
    public void ScopeClearsOldSourceOnEveryAmbiguity(string mode)
    {
        var (element, state) = Scope(); var elements = new List<Element> { element };
        ElementBuilder.ApplyStairNameParameterScopes(elements, state); Assert.NotNull(element.SerializedStairNameParameters);
        var input = state.StairNameParameterRoots[42].Input!;
        switch (mode)
        {
            case "no_root": state.StairNameParameterRoots.Clear(); break;
            case "root_duplicate": state.StairNameParameterRoots[42] = (2, input); break;
            case "null_input": state.StairNameParameterRoots[42] = (1, null); break;
            case "owner_mismatch": state.StairNameParameterRoots[42] = (1, input with { OwnerId = 43 }); break;
            case "incomplete": input.Values.RootParameterStoresComplete = false; break;
            case "no_definition": state.StairNamingDefinitionRoots.Clear(); break;
            case "definition_duplicate": state.StairNamingDefinitionRoots[103] = (2, "IfcName"); break;
            case "definition_null": state.StairNamingDefinitionRoots[103] = (1, null); break;
            case "duplicate_id": input.Values.Strings.Add((103, "own")); break;
            case "cross_store_duplicate": input.Values.Ints.Add((103, 1)); break;
            case "zero_id": input.Values.Strings.Add((0, "")); break;
            case "unknown_bip": input.Values.Strings.Add((int.MinValue, "")); break;
            case "element_duplicate": elements.Add(new() { Id = 42 }); break;
            case "block_only": element.IsBlockOnly = true; break;
            case "wrong_class": element.SerializedOwnElementClassId = 37; break;
        }
        ElementBuilder.ApplyStairNameParameterScopes(elements, state);
        Assert.Null(element.SerializedStairNameParameters);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MalformedDuplicateDefinitionsRemainAmbiguousInBothOrders(bool malformedFirst)
    {
        var bytes = Build(2873).Bytes; var bad = bytes.ToArray(); bad[^1] ^= 1;
        var state = new ParsingState();
        foreach (var data in malformedFirst ? new[] { bad, bytes } : new[] { bytes, bad })
            StairComponentSource.Record(state, data, 0, BitConverter.ToUInt32(data, 8), 2873, 42);
        Assert.Equal(2, state.StairNamingDefinitionRoots[42].BlockCount);
        Assert.Null(state.StairNamingDefinitionRoots[42].Name);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MalformedDuplicateScopesRemainAmbiguousInBothOrders(bool malformedFirst)
    {
        var bytes = Build(3708).Bytes; var bad = bytes.ToArray(); bad[^1] ^= 1;
        var state = new ParsingState();
        foreach (var data in malformedFirst ? new[] { bad, bytes } : new[] { bytes, bad })
            StairComponentSource.Record(state, data, 0, BitConverter.ToUInt32(data, 8), 3708, 42);
        Assert.Equal(2, state.StairNameParameterRoots[42].BlockCount);
        Assert.Null(state.StairNameParameterRoots[42].Input);
    }

    private static (Element, ParsingState) Scope()
    {
        var state = new ParsingState(); var values = new TfBlockDeserializer.NativeParams { RootParameterStoresComplete = true };
        values.Strings.Add((103, "own"));
        state.StairNameParameterRoots[42] = (1, new(42, values)); state.StairNamingDefinitionRoots[103] = (1, "IfcName");
        return (new() { Id = 42, SerializedOwnElementClassId = 3708 }, state);
    }
    private static TfBlockDeserializer.StairNameParameters? Decode(byte[] bytes, ushort cls)
        => TfBlockDeserializer.ExtractStairNameParameters(bytes, 0, BitConverter.ToUInt32(bytes, 8), cls);
    private static string? Definition(byte[] bytes)
        => TfBlockDeserializer.ExtractOwnStringParameterDefinitionName(bytes, 0, BitConverter.ToUInt32(bytes, 8), 2873);

    private static (byte[] Bytes, Dictionary<string, int> Positions) Build(ushort cls, bool stores = false, string caption = "IfcName")
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var positions = new Dictionary<string, int>(); var queue = new List<ushort>();
        writer.Write(42u); writer.Write(0u); writer.Write(0u); writer.Write(cls); Class(cls, "root");
        for (int index = 0; index < queue.Count; index++)
        {
            ushort current = queue[index];
            if (current is 2887 or 2889 or 2886 or 2888)
            {
                positions[$"{current}/count"] = (int)stream.Position; writer.Write(1u);
                switch (current)
                {
                    case 2887: writer.Write(12.5); writer.Write(101); break;
                    case 2889: writer.Write(102); writer.Write(19); break;
                    case 2886: writer.Write(103); positions["2886/length"] = (int)stream.Position; String("literal"); break;
                    case 2888: writer.Write(104); writer.Write(77); break;
                }
            }
            else Class(current, current.ToString());
        }
        positions["end"] = (int)stream.Position;
        writer.Write(0u); var bytes = stream.ToArray(); Frame(bytes); return (bytes, positions);

        void Pointer(ushort target) { writer.Write(uint.MaxValue); writer.Write(target); queue.Add(target); }
        void String(string value) { writer.Write((uint)value.Length); writer.Write(System.Text.Encoding.Unicode.GetBytes(value)); }
        void Class(ushort current, string key)
        {
            foreach (var field in TfBlockDeserializer.SerializationFields(current)!)
            {
                positions[$"{key}/{field.Name}"] = (int)stream.Position;
                if (key == "root" && field.Name == "m_docAccess") { writer.Write(1u); continue; }
                if (key == "root" && field.Name == "m_id" || key == "606" && field.Name == "m_paramElemId") { writer.Write(42); continue; }
                if (key == "root" && stores && field.Name.StartsWith("m_pParamValueSet"))
                {
                    Pointer(field.Name switch { "m_pParamValueSetDouble" => 2887, "m_pParamValueSetInt" => 2889, "m_pParamValueSetAString" => 2886, _ => 2888 }); continue;
                }
                if (key == "root" && field.Name == "m_pParamDef") { Pointer(606); continue; }
                if (field.Name == "m_compatibleCrvInfo") { writer.Write(new byte[21]); continue; }
                if (key == "606" && field.Name == "m_caption") { String(caption); continue; }
                Zero(field);
            }
        }
        void Zero(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 is 5 or 6) { writer.Write(0u); return; }
            if (field.Type == 14)
            {
                if (field.Mods is 16 or 17)
                {
                    for (int index = 0; index < field.Cnt!.Value; index++) Zero(field with { Mods = (byte)(field.Mods & 15), Cnt = null });
                    return;
                }
                if (field.Ref is not null && field.Ref != 20 && field.Mods == 0)
                    foreach (var child in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) Zero(child);
                else writer.Write(0u);
                return;
            }
            int width = field.Type switch { 1 or 2 or 3 => 1, 4 or 5 or 6 => 4, 7 or 10 or 13 => 8, 9 => 16, _ => throw new InvalidOperationException() };
            writer.Write(new byte[width * (field.Mods == 16 ? field.Cnt!.Value : 1)]);
        }
    }
    private static void Frame(byte[] bytes)
    {
        uint size = checked((uint)(bytes.Length - 16));
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
    }
}
