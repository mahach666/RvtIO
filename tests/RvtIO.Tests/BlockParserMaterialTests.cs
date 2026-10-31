using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class BlockParserMaterialTests
{
    [Fact]
    public void MaterialFinish_DecodesStandaloneLayout()
    {
        var data = new byte[16];
        BitConverter.GetBytes(0.85f).CopyTo(data, 8);
        BitConverter.GetBytes(0.50f).CopyTo(data, 12);

        Assert.True(BlockParser.TryDecodeMaterialFinish(
            data, 0, data.Length, out int transparency, out int smoothness));
        Assert.Equal(85, transparency);
        Assert.Equal(50, smoothness);
    }

    [Fact]
    public void MaterialFinish_DecodesAssetBackedPhysicalParamSetLayout()
    {
        var data = new byte[18];
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)2960).CopyTo(data, 4);
        BitConverter.GetBytes(0u).CopyTo(data, 6);
        BitConverter.GetBytes(0.975f).CopyTo(data, 10);
        BitConverter.GetBytes(0.50f).CopyTo(data, 14);

        Assert.True(BlockParser.TryDecodeMaterialFinish(
            data, 0, data.Length, out int transparency, out int smoothness));
        Assert.Equal(97, transparency);
        Assert.Equal(50, smoothness);
    }

    [Fact]
    public void MaterialFinish_KeepsTheStoredFractionNextToTheTruncatedPercent()
    {
        var data = new byte[16];
        BitConverter.GetBytes(0.975f).CopyTo(data, 8);
        BitConverter.GetBytes(0.50f).CopyTo(data, 12);

        Assert.True(BlockParser.TryDecodeMaterialFinish(
            data, 0, data.Length, out int transparency, out _, out float fraction));
        Assert.Equal(97, transparency);
        Assert.Equal(0.975f, fraction);
        Assert.False(BlockParser.TryDecodeMaterialFinish(data, 0, data.Length - 1, out _, out _, out float undecoded));
        Assert.Equal(-1f, undecoded);
    }

    [Fact]
    public void MaterialFinish_DecodesPhysicalParamSetWithNestedParamSet()
    {
        byte[] data = new byte[20];
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)2960).CopyTo(data, 4);
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(data, 6);
        BitConverter.GetBytes((ushort)2880).CopyTo(data, 10);
        BitConverter.GetBytes(0.75f).CopyTo(data, 12);
        BitConverter.GetBytes(0.5f).CopyTo(data, 16);

        Assert.True(BlockParser.TryDecodeMaterialFinish(
            data, 0, data.Length, out int transparency, out int smoothness));
        Assert.Equal(75, transparency);
        Assert.Equal(50, smoothness);
    }

    [Fact]
    public void MaterialFinish_DecodesZeroWordAndNestedParamSetLayout()
    {
        // the ADSK_ materials: [00000000][FFFFFFFF][class 2880][transparency][smoothness]
        var data = new byte[18];
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(data, 4);
        BitConverter.GetBytes((ushort)2880).CopyTo(data, 8);
        BitConverter.GetBytes(0.4f).CopyTo(data, 10);
        BitConverter.GetBytes(0.5f).CopyTo(data, 14);

        Assert.True(BlockParser.TryDecodeMaterialFinish(
            data, 0, data.Length, out int transparency, out int smoothness));
        Assert.Equal(40, transparency);
        Assert.Equal(50, smoothness);
        Assert.False(BlockParser.TryDecodeMaterialFinish(data, 0, data.Length - 1, out _, out _));
    }

    [Fact]
    public void MaterialWithoutAShadingEntry_KeepsTheTransparencyOfItsOwnBlock()
    {
        var info = new RvtIO.Model.RevitFileInfo();
        // the colour of the first comes from its native record, so it has no holder to vote with
        var material = new RvtIO.Model.Element { Id = 700, CategoryId = -2000700, References = [700] };
        var other = new RvtIO.Model.Element { Id = 701, CategoryId = -2000700, References = [701] };
        var notAMaterial = new RvtIO.Model.Element { Id = 702, CategoryId = -2000151 };
        info.Elements.Add(material);
        info.Elements.Add(other);
        info.Elements.Add(notAMaterial);
        var state = new RvtIO.Internal.ParsingState();
        state.MaterialFinishes[700] = (75, 50);
        state.MaterialFinishes[702] = (30, 50);

        ElementBuilder.ApplyMaterialAppearance(info, state);

        Assert.Equal(75, material.Parameters.Single(parameter => parameter.Id == -1002551).Value);
        // a material whose block held no finish gets none (the exporter reads the absent value as 0)
        Assert.DoesNotContain(other.Parameters, parameter => parameter.Id == -1002551);
        Assert.DoesNotContain(notAMaterial.Parameters, parameter => parameter.Id == -1002551);
    }

    [Fact]
    public void MaterialWithADecodedAppearance_KeepsItsTransparencyOverTheFinishOnlyOne()
    {
        var info = new RvtIO.Model.RevitFileInfo();
        var material = new RvtIO.Model.Element { Id = 700, CategoryId = -2000700 };
        info.Elements.Add(material);
        var state = new RvtIO.Internal.ParsingState();
        state.MaterialAppearances[700] = new RvtIO.Internal.MaterialAppearance("Glass", 0xFF0000, 64, 40, 50);
        state.MaterialFinishes[700] = (75, 50);

        ElementBuilder.ApplyMaterialAppearance(info, state);

        Assert.Equal(40, material.Parameters.Single(parameter => parameter.Id == -1002551).Value);
    }

    private static byte[] MaterialBlock(bool withObjectReference, float transparency)
    {
        var bytes = new List<byte>(new byte[0x26]);
        void Text(string value)
        {
            bytes.AddRange(BitConverter.GetBytes(value.Length));
            bytes.AddRange(System.Text.Encoding.Unicode.GetBytes(value));
        }
        Text("textures\\bricks.jpg");   // the appearance asset path, the first string of the large layout
        bytes.AddRange([0x0D, 0xB9, 0xF0, 0xFF, 0x00, 0x00, 0x00, 0x00]);   // the display name tag
        // some large layouts keep an object reference between the tag and the name
        if (withObjectReference) bytes.AddRange([0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xC3, 0x05]);
        Text("XS_Insulation 50");
        bytes.AddRange(new byte[8]);
        bytes.AddRange(BitConverter.GetBytes(transparency));
        bytes.AddRange(BitConverter.GetBytes(0.5f));
        // the shading entry: [FFFFFFFF][0][colour][shininess][holder][FFFFFFFF]
        bytes.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
        bytes.AddRange(BitConverter.GetBytes(0u));
        bytes.AddRange(BitConverter.GetBytes(0x00336699));
        bytes.AddRange(BitConverter.GetBytes(25u));
        bytes.AddRange(BitConverter.GetBytes(1234u));
        bytes.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
        bytes.AddRange(new byte[16]);
        return bytes.ToArray();
    }

    [Theory]
    [InlineData(true, 0.2f, 20)]
    [InlineData(false, 0.3f, 30)]
    public void MaterialAppearance_FindsTheFinishAfterTheDisplayNameWithOrWithoutAnObjectReference(
        bool withObjectReference, float transparency, int expected)
    {
        byte[] block = MaterialBlock(withObjectReference, transparency);
        var appearances = new Dictionary<uint, RvtIO.Internal.MaterialAppearance>();
        var finishes = new Dictionary<uint, (int Transparency, int Smoothness)>();

        BlockParser.ExtractMaterialAppearance(block, 0, block.Length, 7, appearances, finishes);

        var appearance = Assert.Contains(7u, appearances);
        Assert.Equal(expected, appearance.Transparency);
        Assert.Equal(50, appearance.Smoothness);
    }

    // A block whose display name is longer than the 100 characters of a tagged string: the name cannot be anchored, the finish follows
    // the last text field and comes before the header of the appearance asset.
    private static byte[] LongNamedMaterialBlock(float? finishBeforeTheHeader, float finishAfterTheHeader)
    {
        var bytes = new List<byte>(new byte[0x26]);
        void Text(string value)
        {
            bytes.AddRange(BitConverter.GetBytes(value.Length));
            bytes.AddRange(System.Text.Encoding.Unicode.GetBytes(value));
        }
        void Finish(float transparency)
        {
            bytes.AddRange(new byte[8]);
            bytes.AddRange(BitConverter.GetBytes(transparency));
            bytes.AddRange(BitConverter.GetBytes(0.5f));
        }
        Text(new string('N', 125));
        bytes.AddRange([0x18, 0x26, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);   // a field of the material between the text fields
        Text("Comments");
        Text("ADSK_Glass_facade");
        if (finishBeforeTheHeader is { } before) Finish(before);
        // the shading entry: [FFFFFFFF][0][colour][shininess][holder][FFFFFFFF]
        bytes.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
        bytes.AddRange(BitConverter.GetBytes(0u));
        bytes.AddRange(BitConverter.GetBytes(0x00336699));
        bytes.AddRange(BitConverter.GetBytes(25u));
        bytes.AddRange(BitConverter.GetBytes(1234u));
        bytes.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
        Text("assetlibrary_base.fbx");
        bytes.AddRange(new byte[8]);
        Text("generic_glossiness");   // a string of the appearance asset followed by zeros and two floats in 0..1
        Finish(finishAfterTheHeader);
        bytes.AddRange(new byte[16]);
        return bytes.ToArray();
    }

    [Fact]
    public void MaterialAppearance_FindsTheFinishAfterTheLastTextFieldWhenTheDisplayNameIsTooLongToAnchor()
    {
        byte[] block = LongNamedMaterialBlock(finishBeforeTheHeader: 0.3f, finishAfterTheHeader: 0.9f);
        var appearances = new Dictionary<uint, RvtIO.Internal.MaterialAppearance>();
        var finishes = new Dictionary<uint, (int Transparency, int Smoothness)>();

        BlockParser.ExtractMaterialAppearance(block, 0, block.Length, 7, appearances, finishes);

        var appearance = Assert.Contains(7u, appearances);
        Assert.Equal(30, appearance.Transparency);
        Assert.Equal(0.3f, appearance.TransparencyFraction);
        Assert.Equal(50, appearance.Smoothness);
    }

    [Fact]
    public void MaterialAppearance_DoesNotTakeAFinishLookalikeOfTheAppearanceAsset()
    {
        byte[] block = LongNamedMaterialBlock(finishBeforeTheHeader: null, finishAfterTheHeader: 0.9f);
        var appearances = new Dictionary<uint, RvtIO.Internal.MaterialAppearance>();
        var finishes = new Dictionary<uint, (int Transparency, int Smoothness)>();

        BlockParser.ExtractMaterialAppearance(block, 0, block.Length, 7, appearances, finishes);

        var appearance = Assert.Contains(7u, appearances);
        Assert.Equal(-1, appearance.Transparency);   // none decoded: the exporter reads it as 0
        Assert.Equal(-1f, appearance.TransparencyFraction);
    }

    [Fact]
    public void MaterialFinish_RejectsDifferentPointerClass()
    {
        var data = new byte[18];
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)2959).CopyTo(data, 4);
        BitConverter.GetBytes(0u).CopyTo(data, 6);
        BitConverter.GetBytes(0.75f).CopyTo(data, 10);
        BitConverter.GetBytes(0.50f).CopyTo(data, 14);

        Assert.False(BlockParser.TryDecodeMaterialFinish(
            data, 0, data.Length, out _, out _));
    }
}
