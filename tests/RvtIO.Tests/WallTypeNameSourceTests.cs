using System.Text;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class BlockParserTerminalElementNameTests
{
    [Fact]
    public void ExtractTerminalElementName_ChoosesLastValidLengthPrefixedName()
    {
        byte[] payload = Payload(
            Encoded("Wrong early name"),
            Encoded("1234567890"),
            Encoded("Canonical wall type"));

        string? name = BlockParser.ExtractTerminalElementName(
            payload, 0, payload.Length);

        Assert.Equal("Canonical wall type", name);
    }

    [Fact]
    public void ExtractTerminalElementName_IgnoresMalformedTruncatedTail()
    {
        byte[] malformedTail = [.. BitConverter.GetBytes(40u), 0x41, 0x00];
        byte[] payload = Payload(
            Encoded("Canonical wall type"), malformedTail);

        string? name = BlockParser.ExtractTerminalElementName(
            payload, 0, payload.Length);

        Assert.Equal("Canonical wall type", name);
    }

    [Fact]
    public void ExtractTerminalElementName_ReturnsNullWithoutValidName()
    {
        byte[] payload = Payload(
            Encoded("1234567890"),
            Encoded("autodesk.invalid-name"));

        Assert.Null(BlockParser.ExtractTerminalElementName(
            payload, 0, payload.Length));
    }

    [Fact]
    public void ExtractTerminalElementName_AcceptsAuthoredDecimalPrefixedTypeName()
    {
        byte[] payload = Payload(
            Encoded("10.1"),
            Encoded("10.1_ТЕС_Шлифовка по бетону(1)_1"));

        string? name = BlockParser.ExtractTerminalElementName(
            payload, 0, payload.Length);

        Assert.Equal("10.1_ТЕС_Шлифовка по бетону(1)_1", name);
    }

    private static byte[] Encoded(string value) =>
    [
        .. BitConverter.GetBytes((uint)value.Length),
        .. Encoding.Unicode.GetBytes(value),
    ];

    private static byte[] Payload(params byte[][] entries)
    {
        var bytes = new List<byte>();
        foreach (byte[] entry in entries)
        {
            bytes.AddRange(entry);
            bytes.AddRange([0xFF, 0xFF, 0xFF]);
        }
        return [.. bytes];
    }
}

public class ElementBuilderWallTypeNameTests
{
    private const int WallsCategoryId = -2000011;

    [Fact]
    public void PostProcess_PropagatesUniqueOwnBlockNameThroughResolvedWallType()
    {
        var type = Wall(200, 0, "Wrong parsed type");
        var instance = Wall(300, type.Id, "Wrong instance name");
        var info = new RevitFileInfo();
        info.Elements.AddRange([type, instance]);
        var state = new ParsingState();
        state.NativeParams[(uint)instance.Id] = WallNativeParams(type.Id);
        state.CompoundStructureTypeNameSource[(uint)type.Id] =
            (1, "Canonical wall type");

        ElementBuilder.PostProcess(info, state);

        Assert.Equal("Canonical wall type", type.Name);
        Assert.Equal("Canonical wall type", instance.Name);
    }

    [Fact]
    public void PostProcess_ValidatedWallTypeSurvivesLateWrongCategoryTypeParameterOverride()
    {
        var type = Wall(200, 0, "Wrong parsed type");
        var instance = Wall(300, type.Id, "Wrong instance name");
        instance.Parameters.Add(new Parameter
        {
            Id = -1002050,
            StorageType = StorageType.ElementId,
            Value = 900,
        });
        var wrongCategoryTarget = new Element
        {
            Id = 900,
            CategoryId = -2000023,
            Category = "OST_Doors",
            Name = "Wrong door type",
        };
        var info = new RevitFileInfo();
        info.Elements.AddRange([type, instance, wrongCategoryTarget]);
        var state = new ParsingState();
        state.NativeParams[(uint)instance.Id] = WallNativeParams(type.Id);
        state.CompoundStructureTypeNameSource[(uint)type.Id] =
            (1, "Canonical wall type");
        state.ElementNameTable[(uint)wrongCategoryTarget.Id] =
            "Wrong late table name";

        ElementBuilder.PostProcess(info, state);

        Assert.Equal(type.Id, instance.TypeId);
        Assert.Equal("Canonical wall type", type.Name);
        Assert.Equal("Canonical wall type", instance.Name);
    }

    [Fact]
    public void PostProcess_ValidatedWallTypeDoesNotLeakToSiblingWithoutOwnSerializedLink()
    {
        var type = Wall(200, 0, "Existing type name");
        var validatedInstance = Wall(300, type.Id, "Existing validated name");
        var unvalidatedSibling = Wall(301, type.Id, "Existing sibling name");
        var info = new RevitFileInfo();
        info.Elements.AddRange([type, validatedInstance, unvalidatedSibling]);
        var state = new ParsingState();
        state.NativeParams[(uint)validatedInstance.Id] =
            WallNativeParams(type.Id);
        state.CompoundStructureTypeNameSource[(uint)type.Id] =
            (1, "Canonical wall type");

        ElementBuilder.PostProcess(info, state);

        Assert.Equal("Canonical wall type", type.Name);
        Assert.Equal("Canonical wall type", validatedInstance.Name);
        Assert.Equal("Existing type name", unvalidatedSibling.Name);
    }

    [Theory]
    [InlineData("wrong-category")]
    [InlineData("self-type")]
    [InlineData("missing-source")]
    [InlineData("multiple-blocks")]
    [InlineData("missing-terminal-name")]
    [InlineData("missing-serialized-type")]
    public void ApplyValidatedWallTypeNames_FailsClosedWithoutCompleteValidation(
        string brokenValidation)
    {
        var type = Wall(200, 0, "Existing type name");
        var instance = Wall(300, type.Id, "Existing instance name");
        var elements = new List<Element> { type, instance };
        var source = new Dictionary<uint,
            (int BlockCount, string? TerminalName)>
        {
            [(uint)type.Id] = (1, "Unvalidated replacement"),
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)instance.Id] = WallNativeParams(type.Id),
        };

        switch (brokenValidation)
        {
            case "wrong-category":
                type.CategoryId = -2000014;
                break;
            case "self-type":
                instance.TypeId = instance.Id;
                nativeParams[(uint)instance.Id] =
                    WallNativeParams(instance.Id);
                source[(uint)instance.Id] =
                    (1, "Unvalidated replacement");
                break;
            case "missing-source":
                source.Clear();
                break;
            case "multiple-blocks":
                source[(uint)type.Id] =
                    (2, "Unvalidated replacement");
                break;
            case "missing-terminal-name":
                source[(uint)type.Id] = (1, null);
                break;
            case "missing-serialized-type":
                nativeParams.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(brokenValidation));
        }

        ElementBuilder.ApplyValidatedWallTypeNames(
            elements, elements.ToDictionary(element => element.Id), source,
            nativeParams);

        Assert.Equal("Existing type name", type.Name);
        Assert.Equal("Existing instance name", instance.Name);
    }

    [Fact]
    public void ApplyValidatedWallTypeNames_UsesSerializedTypeInsteadOfPostProcessedTypeId()
    {
        var serializedType = Wall(200, 0, "Wrong serialized type name");
        var postProcessedType = Wall(250, 0, "Post-processed type name");
        var instance = Wall(300, postProcessedType.Id, "Wrong instance name");
        var elements = new List<Element>
        {
            serializedType, postProcessedType, instance,
        };
        var source = new Dictionary<uint,
            (int BlockCount, string? TerminalName)>
        {
            [(uint)serializedType.Id] = (1, "Serialized wall type"),
            [(uint)postProcessedType.Id] = (1, "Wrong inferred wall type"),
        };
        var nativeParams = new Dictionary<uint, TfBlockDeserializer.NativeParams>
        {
            [(uint)instance.Id] = WallNativeParams(serializedType.Id),
        };

        ElementBuilder.ApplyValidatedWallTypeNames(
            elements, elements.ToDictionary(element => element.Id), source,
            nativeParams);

        Assert.Equal("Serialized wall type", serializedType.Name);
        Assert.Equal("Post-processed type name", postProcessedType.Name);
        Assert.Equal("Serialized wall type", instance.Name);
    }

    private static TfBlockDeserializer.NativeParams WallNativeParams(int typeId)
    {
        var nativeParams = new TfBlockDeserializer.NativeParams();
        nativeParams.Fields["m_WallAttributesId"] = typeId;
        return nativeParams;
    }

    private static Element Wall(int id, int typeId, string name) => new()
    {
        Id = id,
        TypeId = typeId,
        CategoryId = WallsCategoryId,
        Category = "OST_Walls",
        Name = name,
    };
}
