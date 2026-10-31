using System.Text;
using System.Text.Json;
using RvtIO.Compact;
using Xunit;

namespace RvtIO.Tests;

public class CompactJsonWriterTests
{
    private static byte[] WriteBytes(params CompactNode[] nodes)
    {
        using var stream = new MemoryStream();
        using (var writer = new CompactJsonWriter(stream))
        {
            foreach (var node in nodes) writer.WriteNode(node);
            writer.Complete();
        }
        return stream.ToArray();
    }

    private static string Write(params CompactNode[] nodes) => Encoding.UTF8.GetString(WriteBytes(nodes));

    private static CompactNode Node(int id, params CompactProperty[] properties)
        => new(id, "SWall", $"uid-{id}", null, null, properties);

    private static CompactProperty P(string group, string label, string value) => new(group, label, value);

    [Fact]
    public void ClassNamePrefix_IsPutInFrontOfANonEmptyClassNameOnly()
    {
        using var stream = new MemoryStream();
        using (var writer = new CompactJsonWriter(stream, "Cls"))
        {
            writer.WriteNode(Node(1));
            writer.WriteNode(new CompactNode(2, "", "uid-2", null, null, []));
            writer.Complete();
        }
        string json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"object\":\"1\",\"name\":\"ClsSWall\"", json);
        Assert.Contains("\"object\":\"2\",\"name\":\"\"", json);
    }

    [Fact]
    public void ClassNamePrefix_RefusesNull()
    {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => new CompactJsonWriter(stream, null!));
    }

    [Fact]
    public void EmptyCollection_IsTheDocumentWrapperOnly()
        => Assert.Equal("{\"data\":{\"type\":\"properties\",\"collection\":[]}}", Write());

    [Fact]
    public void NodeWithoutProperties_WritesAnEmptyPropertiesArray()
        => Assert.Equal(
            "{\"data\":{\"type\":\"properties\",\"collection\":["
            + "{\"object\":\"243274\",\"name\":\"SWall\",\"externalId\":\"uid-243274\",\"properties\":[]}"
            + "]}}",
            Write(Node(243274)));

    [Fact]
    public void GroupsAndLabels_AreSortedByCodeUnitAndWrittenAsGroupNameThenObject()
    {
        // Inserted out of order, with a lower-case name that an ordinal sort puts after every capital.
        string json = Write(Node(7,
            P("Phasing", "Phase Created", "New Construction"),
            P("Invalid group", "Family Name", ""),
            P("Identity Data", "Type Name", "Basic Wall"),
            P("Identity Data", "Design Option", "None"),
            P("IFC Parameters", "IfcGUID", "g"),
            P("Invalid group", "Category", "OST_Walls"),
            P("Identity Data", "a lower case label", "x"),
            P("Identity Data", "Mark", "m"),
            P("zeta", "L", "v")));
        Assert.Equal(
            "{\"data\":{\"type\":\"properties\",\"collection\":[{\"object\":\"7\",\"name\":\"SWall\",\"externalId\":\"uid-7\",\"properties\":["
            + "\"IFC Parameters\",{\"IfcGUID\":\"g\"},"
            + "\"Identity Data\",{\"Design Option\":\"None\",\"Mark\":\"m\",\"Type Name\":\"Basic Wall\",\"a lower case label\":\"x\"},"
            + "\"Invalid group\",{\"Category\":\"OST_Walls\",\"Family Name\":\"\"},"
            + "\"Phasing\",{\"Phase Created\":\"New Construction\"},"
            + "\"zeta\",{\"L\":\"v\"}"
            + "]}]}}",
            json);
    }

    [Fact]
    public void Order_IsByUtf16CodeUnit_NotByCodePointOrCulture()
    {
        // U+1F600 is the surrogate pair D83D DE00, which sorts before U+FFFD in code units (and after it by code point).
        string supplementary = char.ConvertFromUtf32(0x1F600);
        using var doc = JsonDocument.Parse(Write(Node(1, P("\uFFFD", "a", "1"), P(supplementary, "a", "2"), P("e", "a", "3"), P("é", "a", "4"))));
        var groups = doc.RootElement.GetProperty("data").GetProperty("collection")[0].GetProperty("properties")
            .EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).ToArray();
        Assert.Equal(new[] { "e", "é", supplementary, "\uFFFD" }, groups);
    }

    [Fact]
    public void RepeatedGroupAndLabel_KeepsTheFirstInsertedValue()
    {
        string json = Write(Node(1,
            P("G", "L", "first"),
            P("G", "K", "k"),
            P("G", "L", "second"),
            P("H", "L", "other group keeps its own"),
            P("G", "L", "third")));
        Assert.Contains("\"properties\":[\"G\",{\"K\":\"k\",\"L\":\"first\"},\"H\",{\"L\":\"other group keeps its own\"}]", json);
        Assert.DoesNotContain("second", json);
        Assert.DoesNotContain("third", json);
    }

    [Fact]
    public void ManyEqualKeys_KeepTheFirstValue_WhateverTheSortDoes()
    {
        // 200 entries, one key, to leave the insertion-sort range of the introsort.
        var properties = Enumerable.Range(0, 200).Select(i => P("G", "L", $"v{i}")).ToArray();
        Assert.Contains("\"properties\":[\"G\",{\"L\":\"v0\"}]", Write(Node(1, properties)));
    }

    [Theory]
    [InlineData("Walls", "Basic Wall", true)]
    [InlineData("Walls", "", false)]
    [InlineData("Walls", null, false)]
    [InlineData("", "Basic Wall", false)]
    [InlineData(null, "Basic Wall", false)]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    public void PseudoObjects_AreWrittenOnlyWhenCategoryLabelAndFamilyNameAreBothNonEmpty(string? category, string? family, bool written)
    {
        string json = Write(new CompactNode(5, "SWall", "u", category, family, []));
        string element = "{\"object\":\"5\",\"name\":\"SWall\",\"externalId\":\"u\",\"properties\":[]}";
        string expected = written
            ? $"{{\"objectId\":\"\",\"name\":\"{category}\",\"externalId\":\"\"}},{{\"objectId\":\"\",\"name\":\"{family}\",\"externalId\":\"\"}},{element}"
            : element;
        Assert.Equal("{\"data\":{\"type\":\"properties\",\"collection\":[" + expected + "]}}", json);
    }

    [Fact]
    public void Nodes_AreWrittenInTheOrderTheyAreGiven()
    {
        string json = Write(Node(3), Node(11), Node(12));
        using var doc = JsonDocument.Parse(json);
        var ids = doc.RootElement.GetProperty("data").GetProperty("collection").EnumerateArray()
            .Select(node => node.GetProperty("object").GetString()).ToArray();
        Assert.Equal(new[] { "3", "11", "12" }, ids);
    }

    [Fact]
    public void NegativeAndExtremeIds_ArePrintedAsDecimalStrings()
    {
        string json = Write(Node(-5), Node(int.MaxValue), Node(int.MinValue));
        Assert.Contains("\"object\":\"-5\"", json);
        Assert.Contains("\"object\":\"2147483647\"", json);
        Assert.Contains("\"object\":\"-2147483648\"", json);
    }

    [Fact]
    public void Output_IsMinifiedUtf8_WithoutByteOrderMarkOrTrailingNewline()
    {
        byte[] bytes = WriteBytes(Node(1, P("G", "L", "v")));
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal((byte)'}', bytes[^1]);
        Assert.DoesNotContain((byte)'\n', bytes);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.DoesNotContain((byte)' ', bytes);
    }

    [Fact]
    public void Cyrillic_StaysRaw_AndRoundTrips()
    {
        const string group = "Свойства";
        const string label = "Имя типа";
        const string value = "Стена наружная 380 мм";
        byte[] bytes = WriteBytes(new CompactNode(1, "SWall", "u", "Стены", "Базовая стена", [P(group, label, value)]));
        string text = Encoding.UTF8.GetString(bytes);
        Assert.Contains(value, text);
        Assert.Contains("\"Стены\"", text);
        Assert.DoesNotContain("\\u04", text);
        using var doc = JsonDocument.Parse(bytes);
        var collection = doc.RootElement.GetProperty("data").GetProperty("collection");
        Assert.Equal("Стены", collection[0].GetProperty("name").GetString());
        Assert.Equal("Базовая стена", collection[1].GetProperty("name").GetString());
        Assert.Equal(value, collection[2].GetProperty("properties")[1].GetProperty(label).GetString());
    }

    [Fact]
    public void QuotesBackslashesAndControlCharacters_AreEscapedAndRoundTrip()
    {
        const string value = "a\"b\\c\u0001\n\r\t\b\f\u001F end";
        byte[] bytes = WriteBytes(Node(1, P("G\"1", "L\\2\n", value)));
        Assert.DoesNotContain(bytes, b => b < 0x20);
        using var doc = JsonDocument.Parse(bytes);
        var properties = doc.RootElement.GetProperty("data").GetProperty("collection")[0].GetProperty("properties");
        Assert.Equal("G\"1", properties[0].GetString());
        Assert.Equal(value, properties[1].GetProperty("L\\2\n").GetString());
    }

    [Fact]
    public void LoneSurrogate_IsWrittenAsTheReplacementCharacterEscape_NeverAsInvalidUtf8()
    {
        byte[] bytes = WriteBytes(Node(4711, P("G", "L", "bad " + (char)0xD800 + " value")));
        using var doc = JsonDocument.Parse(bytes);
        var value = doc.RootElement.GetProperty("data").GetProperty("collection")[0].GetProperty("properties")[1].GetProperty("L").GetString();
        Assert.Equal("bad " + (char)0xFFFD + " value", value);
    }

    [Fact]
    public void NoBreakSpaceAndAstralCharacters_RoundTripWhateverTheirEscaping()
    {
        string value = "12" + (char)0xA0 + "m" + (char)0xB2 + " " + char.ConvertFromUtf32(0x1F600) + " " + (char)0x2028;
        using var doc = JsonDocument.Parse(WriteBytes(Node(1, P("G", "L", value))));
        Assert.Equal(value, doc.RootElement.GetProperty("data").GetProperty("collection")[0].GetProperty("properties")[1].GetProperty("L").GetString());
    }

    [Fact]
    public void TwoWrites_OfTheSameNodes_AreByteIdentical()
    {
        var nodes = Enumerable.Range(1, 500).Select(i => new CompactNode(i, "FamilyInstance", $"u{i}", "Doors", "Single", new[]
        {
            P("Phasing", "Phase Created", "New Construction"),
            P("Identity Data", $"Mark {i % 7}", i.ToString()),
            P("Identity Data", $"Mark {i % 7}", "shadowed"),
            P("Constraints", "Level", "Level 1"),
        })).ToArray();
        Assert.Equal(WriteBytes(nodes), WriteBytes(nodes));
    }

    [Fact]
    public void NullPartsOfANodeOrProperty_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => Write(new CompactNode(1, null!, "u", null, null, [])));
        Assert.Throws<ArgumentException>(() => Write(new CompactNode(1, "c", null!, null, null, [])));
        Assert.Throws<ArgumentException>(() => Write(new CompactNode(1, "c", "u", null, null, null!)));
        Assert.Throws<InvalidDataException>(() => Write(Node(1, new CompactProperty(null!, "L", "v"))));
        Assert.Throws<InvalidDataException>(() => Write(Node(1, new CompactProperty("G", null!, "v"))));
        Assert.Throws<InvalidDataException>(() => Write(Node(1, new CompactProperty("G", "L", null!))));
    }

    [Fact]
    public void WritingAfterComplete_Throws()
    {
        using var stream = new MemoryStream();
        using var writer = new CompactJsonWriter(stream);
        writer.Complete();
        Assert.Throws<InvalidOperationException>(() => writer.WriteNode(Node(1)));
    }

    [Fact]
    public void AMillionNodes_AreStreamedIntoACountingStreamWithoutBeingHeld()
    {
        var stream = new CountingStream();
        // One property array shared by every node: the test must not be the thing that holds a million lists.
        CompactProperty[] properties =
        [
            P("Identity Data", "Type Name", "Basic Wall: Generic - 200mm"),
            P("Phasing", "Phase Created", "New Construction"),
            P("Invalid group", "Category", "OST_Walls"),
        ];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long written;
        using (var writer = new CompactJsonWriter(stream))
        {
            for (int id = 1; id <= 1_000_000; id++)
                writer.WriteNode(new CompactNode(id, "SWall", "e3e052f9-0156-11d5-9301-0000863f27ad-0000abcd", "Walls", "Basic Wall", properties));
            writer.Complete();
            written = writer.BytesWritten;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(written, stream.Length);
        Assert.True(written > 300_000_000, $"the synthetic document is {written} bytes");
        // The pending bytes are handed over once CompactJsonWriter.FlushThreshold is reached: every chunk stays small ...
        Assert.True(stream.LargestWrite <= 4 * CompactJsonWriter.FlushThreshold, $"largest write {stream.LargestWrite}");
        Assert.True(stream.Writes >= written / (4 * CompactJsonWriter.FlushThreshold), $"{stream.Writes} writes");
        // ... and nothing proportional to the document is allocated (holding it would take more than 300 MB).
        Assert.True(allocated < 64L * 1024 * 1024, $"allocated {allocated} bytes");
    }

    private sealed class CountingStream : Stream
    {
        public long Length1;
        public int Writes;
        public int LargestWrite;

        public override long Length => Length1;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Position { get => Length1; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Count(count);
        public override void Write(ReadOnlySpan<byte> buffer) => Count(buffer.Length);

        private void Count(int count)
        {
            Length1 += count;
            Writes++;
            LargestWrite = Math.Max(LargestWrite, count);
        }
    }
}
