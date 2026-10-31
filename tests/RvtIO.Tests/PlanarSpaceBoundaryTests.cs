using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class PlanarSpaceBoundaryTests
{
    // Complete 2,367-byte GElement cache: one five-face planar closed prism. Geometry only; no strings, names or paths.
    private const string Fixture = "N1hxAG1gqUsvCQAA1wc3WHEAAAAAAP////8EkAgAAQAAAAMAAAAsCJ7vM+pmKE1AArbq+rIfN0AhkUgkEgk9QO0YJvoDflBAdN+lkRs3Q0CQSCQSiYRCQJ7vM+pmKE1AArbq+rIfN0AhkUgkEgk9QO0YJvoDflBAdN+lkRs3Q0CQSCQSiYRCQAMAAAAAAAAA/////0EQAhH/////BMAIAAUAAAAEAAAANQYFAAAANQYGAAAANQYHAAAANQYIAAAANQYHAAAA/////wAAAAABAAAACQAAAAkAAADbBAoAAADbBAsAAADbBAwAAADbBA0AAADbBA4AAADbBA8AAADbBBAAAADbBBEAAADbBAAAAAAAAAAAAAAAAKcBAAAEgghAEgAAAOYEAAAAAAAAAAAAAAAAAAAAAAQAAAD19sL/EwAAAAoCAQAAAAAAAACnAQAABIIIQBQAAADmBAAAAAAAAAAAAAAAAAAAAAAEAAAA9fbC/xUAAAAKAgIAAAAAAAAApwEAAASCCEAWAAAA5gQAAAAAAAAAAAAAAAAAAAAABAAAAPX2wv8XAAAACgIDAAAAAAAAAKcBAAAEgghAGAAAAOYEAAAAAAAAAAAAAAAAAAAAAAQAAAD19sL/GQAAAAoCBAAAAAAAAACnAQAABIIIQBoAAADmBAAAAAAAAAAAAAAAAAAAAAAEAAAA9fbC/xsAAAAKAgcAAAAAAAAA/////wSCCAAEAAAABgAAAAoAAAARAAAAEgAAABYAAAAAAAAA8RGQwvhh7L8AAAAAAIDlvAAAAAAAAAAAAAAAAAAAAIBAh1feft8jQAAAAAAAAOC8AAAAAAAAAIBgiIBqnqUlQAYIAAAAAAAAAP////8EgggABAAAAAcAAAALAAAADwAAAAkAAAAYAAAAAAAAADyHV95+3yNAAAAAAAAA6DwAAAAAAAAAAAAAAAAAAAAAPIdX3n7fI0AqiIBqnqUlQAAAAAAAAAAAKIiAap6lJUAGCQAAAAAAAAD/////BIIIAAQAAAAIAAAAEgAAABAAAAAKAAAAGgAAAAAAAAA7h1feft8jQCmIgGqepSVAAAAAAAAAAAAAAAAAAAAAAPERkML4Yey/AAAAAACA5bwAAAAAAAAAAMwRwlAInS5ABg4AAAAAAAAA/////wSCCAAFAAAACAAAAA0AAAARAAAAFAAAABAAAAAAAAAA8RGQwvhh7D8AAAAAAIDlvPz//////x9AzBHCUAidLkA7h1feft8jwCmIgGqepSVA/P//////H0AAAAAAAAAAAAYPAAAAAAAAAP////8EgggABQAAAAcAAAAOAAAAEAAAAAwAAAAPAAAAAAAAADyHV95+3yPAKoiAap6lJUD8//////8fQCiIgGqepSVAPIdX3n7fI8AAAAAAAADoPPz//////x9AAAAAAAAAAAAGEAAAAAAAAAD/////BIIIAAUAAAAGAAAAFAAAAA8AAAANAAAAEQAAAAAAAABAh1feft8jwAAAAAAAAOC8/P//////H0BgiIBqnqUlQPERkML4Yew/AAAAAACA5bz8//////8fQAAAAAAAAAAABhIAAAAAAAAA/////wSCCAAHAAAABgAAAA0AAAAWAAAACgAAAA4AAAAAAAAA/P//////H0AAAAAAAAAAAPz//////x9AXIiAap6lJUAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIBciIBqnqUlQAcTAAAAAAAAAP////8EgggACAAAAAcAAAAMAAAAGAAAAAsAAAANAAAAAAAAAPz//////x9AAAAAAAAAAAD8//////8fQCiIgGqepSVAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKIiAap6lJUAHGAAAAAAAAAD/////BIIIAAYAAAAIAAAADgAAABoAAAAJAAAADAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAAAAAAAAAADMEcJQCJ0uQPz//////x9AAAAAAAAAAAD8//////8fQMwRwlAInS5ABv////8AAAAA/////wQACAAAAAAABAAAAAkAAAALAAAA8RGQwvhh7L8AAAAAAIDlvECHV95+3yNAKoiAap6lJUAA8RGQwvhh7L8AAAAAAIDlvECHV95+3yNAKoiAap6lJUABba5lV+BVUEBzCiVM1OZCQJBIJBKJhEJAujt/Zp6g5r/gO39mnqDmvwAAAAAAAAAA4Dt/Zp6g5j+6O39mnqDmvwAAAAAAAAAA/////wAAAAD/////BAAIAAAAAAAFAAAADAAAAA4AAABAh1feft8jwAAAAAAAgOW88RGQwvhh7D8qiIBqnqUlQABAh1feft8jwAAAAAAAgOW88RGQwvhh7D8qiIBqnqUlQAFtrmVX4FVQQHMKJUzU5kJAIZFIJBIJPUC6O39mnqDmP+A7f2aeoOY/AAAAAAAAAIDgO39mnqDmP7o7f2aeoOa/AAAAAAAAAAD/////AAAAAP////8EAAgAAAAAAAYAAAAJAAAADwAAAAAAAAAAAAAAAAAAAAAAAID8//////8fQGCIgGqepSVAAAAAAAAAAAAAAAAAAAAAAID8//////8fQGCIgGqepSVAAe0YJvoDflBAdN+lkRs3Q0CQSCQSiYRCQAAAAAAAAAAAAAAAAAAAAAAAAAAAAADwv7o7f2aeoOa/4Dt/Zp6g5r8AAAAAAAAAgP////8AAAAA/////wQACAAAAAAABwAAAAoAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAPz//////x9AKIiAap6lJUAAAAAAAAAAAAAAAAAAAAAAAPz//////x9AKIiAap6lJUABoO8z6mYoTUBlOhsP9cY+QJBIJBKJhEJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPC/4Tt/Zp6g5j+4O39mnqDmvwAAAAAAAAAA/////wAAAAD/////BAAIAAAAAAAIAAAACwAAABEAAAAAAAAAAAAAAAAAAAAAAAAA/P//////H0DMEcJQCJ0uQAAAAAAAAAAAAAAAAAAAAAAA/P//////H0DMEcJQCJ0uQAHsGCb6A35QQAK26vqyHzdAkEgkEomEQkAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8L8RjpQ0867SPAAAAAAAAPA/AAAAAAAAAAAvCQAA";
    private static byte[] Block() => Convert.FromBase64String(Fixture);
    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);
    private static GRepData? Decode(byte[] block, uint owner = 7428151) =>
        GRepParserV2.ParseExactPlanarSpaceBoundary(block, 0, Size(block), owner);

    [Fact]
    public void CompleteSuppressedPlanarCache_IsSpatialOnly()
    {
        var block = Block();
        var boundary = Assert.IsType<GRepData>(Decode(block));
        Assert.Equal(5, boundary.Faces.Count);
        Assert.True(Assert.Single(boundary.Solids).IsClosed);
        Assert.Equal(5, boundary.Solids[0].FaceIndices.Count);
        Assert.Null(GRepParserV2.Parse(block, 0, Size(block)));
        Assert.False(GRepParserV2.IsPhysicalBodySurface(0x40088204u));
        Assert.Equal(Block(), block); // source flags were not rewritten
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("footer")]
    [InlineData("size")]
    [InlineData("class")]
    [InlineData("truncated")]
    [InlineData("tail")]
    [InlineData("unsuppressed")]
    [InlineData("auxiliary")]
    [InlineData("geometry-state")]
    [InlineData("unknown-class")]
    [InlineData("duplicate-face")]
    [InlineData("missing-adjacent-face")]
    [InlineData("nonfinite-plane")]
    public void IncompleteAmbiguousOrUnsupportedCache_FailsClosed(string mutation)
    {
        byte[] block = Block();
        switch (mutation)
        {
            case "owner": Assert.Null(Decode(block, 100)); return;
            case "footer": block[^1] ^= 1; break;
            case "size": block[8] ^= 1; break;
            case "class": block[12] ^= 1; break;
            case "truncated": block = block[..^1]; break;
            case "tail":
                uint size = Size(block) + 1;
                block = [.. block[..^4], 0, .. BitConverter.GetBytes(size)];
                BitConverter.GetBytes(size).CopyTo(block, 8); break;
            case "unsuppressed": block[272 + 15] &= 0xBF; break;
            case "auxiliary": block[272 + 15] |= 4; break;
            case "geometry-state": block[144 + 13] &= 0xBF; break;
            case "unknown-class": BitConverter.GetBytes((ushort)983).CopyTo(block, 322); break;
            case "duplicate-face": BitConverter.GetBytes(4).CopyTo(block, 176); break;
            case "missing-adjacent-face": BitConverter.GetBytes(999).CopyTo(block, 532 + 16); break;
            case "nonfinite-plane": BitConverter.GetBytes(double.NaN).CopyTo(block, 1578 + 33); break;
        }
        Assert.Null(Decode(block));
    }

    [Fact]
    public void Framing_AllowsOffsetButNotAnOverclaimedBuffer()
    {
        byte[] block = Block();
        byte[] padded = [0, 0, 0, .. block, 0, 0];
        Assert.NotNull(GRepParserV2.ParseExactPlanarSpaceBoundary(padded, 3, Size(block), 7428151));
        Assert.Null(GRepParserV2.ParseExactPlanarSpaceBoundary(padded, -1, Size(block), 7428151));
        Assert.Null(GRepParserV2.ParseExactPlanarSpaceBoundary(padded, 3, uint.MaxValue, 7428151));
    }

    [Fact]
    public void EveryTruncatedPrefix_IsRejected()
    {
        byte[] block = Block();
        for (int length = 0; length < block.Length; length++)
            Assert.Null(GRepParserV2.ParseExactPlanarSpaceBoundary(block[..length], 0, Size(block), 7428151));
        // A matching footer alone is insufficient: no framed partial queue may pass.
        for (int prefix = 16; prefix < block.Length - 4; prefix++)
        {
            uint size = (uint)(prefix - 12);
            byte[] framed = [.. block[..prefix], .. BitConverter.GetBytes(size)];
            BitConverter.GetBytes(size).CopyTo(framed, 8);
            Assert.Null(Decode(framed));
        }
    }

    [Fact]
    public void Recorder_CountsEveryBlockAndNeverRestoresAmbiguousSource()
    {
        var block = Block();
        var state = new ParsingState();
        RvtFileReader.RecordSpaceBoundarySource(state, block, 0, Size(block), 2007, 7428151);
        Assert.NotNull(state.SpaceBoundarySource[7428151].Boundary);
        RvtFileReader.RecordSpaceBoundarySource(state, block, 0, Size(block), 2007, 7428151);
        Assert.Null(state.SpaceBoundarySource[7428151].Boundary);
        Assert.Equal(2, state.SpaceBoundarySource[7428151].BlockCount);
        state = new ParsingState();
        RvtFileReader.RecordSpaceBoundarySource(state, block[..^1], 0, Size(block), 2007, 7428151);
        RvtFileReader.RecordSpaceBoundarySource(state, block, 0, Size(block), 2007, 7428151);
        Assert.Null(state.SpaceBoundarySource[7428151].Boundary);
    }

    [Theory]
    [InlineData(-2000160, 1, 1, true)]
    [InlineData(-2003200, 1, 1, false)]
    [InlineData(-2000011, 1, 1, false)]
    [InlineData(-2000160, 2, 1, false)]
    [InlineData(-2000160, 1, 2, false)]
    public void Builder_RequiresUniqueRoomRootAndGeometryWithoutChangingPhysicalGeometry(
        int category, int roots, int blocks, bool expected)
    {
        var element = new Element { Id = 7428151, CategoryId = category };
        var physical = new GeometryData();
        element.Geometry = physical;
        ElementBuilder.ApplySerializedSpaceBoundaries([element],
            new Dictionary<uint, (int, string?)> { [7428151] = (roots, "Room") },
            new Dictionary<uint, (int, GRepData?)> { [7428151] = (blocks, Decode(Block())) });
        Assert.Equal(expected, element.SerializedSpaceBoundary != null);
        Assert.Same(physical, element.Geometry);
        Assert.DoesNotContain("SerializedSpaceBoundary", System.Text.Json.JsonSerializer.Serialize(element));
        ElementBuilder.ApplySerializedSpaceBoundaries([element],
            new Dictionary<uint, (int, string?)>(), new Dictionary<uint, (int, GRepData?)>());
        Assert.Null(element.SerializedSpaceBoundary);
    }
}
