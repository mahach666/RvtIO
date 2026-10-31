using System.Reflection;
using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class IfcMaterialNameTests
{
    [Fact]
    public void OwnMaterialName_PrecedesTaggedAssetAndPreferredNames_WithoutPublicMutation()
    {
        var material = Material();
        material.References = [2];
        material.Parameters.Add(Text("MATERIAL_NAME", "Asset tagged name"));
        var asset = new Element { Id = 2, TypeId = material.TypeId, Name = "Asset name" };
        var elements = new Dictionary<int, Element> { [1] = material, [2] = asset };
        string before = JsonSerializer.Serialize(elements);
        Assert.Equal("Own authored name", IfcExporter.ResolveMaterialName(1, "Layer preferred name", elements));
        Assert.Equal(before, JsonSerializer.Serialize(elements));
    }

    [Theory]
    [InlineData("Rendering appearance not upgraded")]
    [InlineData("GenericSchema")]
    [InlineData("  ")]
    [InlineData("Авторский 'материал'")]
    public void ExactAuthoredName_IsNotCleanedOrFilteredAsAssetMetadata(string name)
    {
        var material = Material();
        material.SerializedMaterialName = name;
        Assert.Equal(name, Resolve(material));
    }

    [Theory]
    [InlineData("IfcName")]
    [InlineData("NameOverride")]
    [InlineData("ifc name")]
    public void AnyOverrideParameter_PreservesLegacyPath(string name)
    {
        foreach (object? value in new object?[] { null, "", "Override", 7 })
        {
            var material = Material();
            material.Parameters.Add(new Parameter { Definition = new Definition { Name = name },
                StorageType = StorageType.String, Value = value });
            Assert.Equal("Legacy name", Resolve(material));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingOwnValidation_RetainsPreviousTaggedName(string? own)
    {
        var material = Material();
        material.SerializedMaterialName = own;
        material.Parameters.Add(Text("MATERIAL_NAME", "Tagged material"));
        Assert.Equal("Tagged material", Resolve(material));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidOrMismatchedOwnerCannotSupplyOwnName(int id)
    {
        var material = Material();
        material.Id = id;
        Assert.Equal("Legacy name", Resolve(material));
    }

    [Fact]
    public void MissingMaterial_UsesExistingPreferredOrGeneratedName()
    {
        var elements = new Dictionary<int, Element>();
        Assert.Equal("Preferred", IfcExporter.ResolveMaterialName(1, "Preferred", elements));
        Assert.Equal("Material 1", IfcExporter.ResolveMaterialName(1, null, elements));
    }

    [Theory]
    [InlineData("PlasterboardMaterialBlock")]
    [InlineData("SampleTexturedMaterialBlock")]
    public void MaterialRoot_OwnerMustAgreeWithHeaderAndWalker(string fixture)
    {
        byte[] block = MaterialBlock(fixture);
        uint size = BitConverter.ToUInt32(block, 8);
        uint owner = BitConverter.ToUInt32(block, 0);
        Assert.NotNull(TfBlockDeserializer.ExtractMaterialName(block, 0, size, 2486));
        var state = new ParsingState();
        RvtIO.RvtFileReader.RecordMaterialName(state, block, 0, size, 2486, owner + 1);
        Assert.Null(state.MaterialNames[owner + 1].Name);
        block[0] ^= 1; // Complete body and footer, but own m_id still names the original owner.
        Assert.Null(TfBlockDeserializer.ExtractMaterialName(block, 0, size, 2486));
        BitConverter.GetBytes(0).CopyTo(block, 0);
        Assert.Null(TfBlockDeserializer.ExtractMaterialName(block, 0, size, 2486));
    }

    [Theory]
    [InlineData("duplicate_root")]
    [InlineData("unreadable_first_root")]
    [InlineData("duplicate_element")]
    public void DuplicateOrUnreadableSource_IsNotRepairedByLaterNames(string mode)
    {
        byte[] block = MaterialBlock("PlasterboardMaterialBlock");
        uint size = BitConverter.ToUInt32(block, 8), owner = BitConverter.ToUInt32(block, 0);
        var state = new ParsingState();
        if (mode == "unreadable_first_root")
        {
            byte[] bad = (byte[])block.Clone();
            bad[^1] ^= 1;
            RvtIO.RvtFileReader.RecordMaterialName(state, bad, 0, size, 2486, owner);
        }
        RvtIO.RvtFileReader.RecordMaterialName(state, block, 0, size, 2486, owner);
        if (mode == "duplicate_root")
            RvtIO.RvtFileReader.RecordMaterialName(state, block, 0, size, 2486, owner);
        var material = Material();
        material.Id = (int)owner;
        var elements = new List<Element> { material };
        if (mode == "duplicate_element") elements.Add(new Element { Id = material.Id });
        ElementBuilder.ApplyCeilingFinishSource(elements, state);
        Assert.Null(material.SerializedMaterialName);
    }

    private static byte[] MaterialBlock(string fixture) => Convert.FromBase64String(
        (string)typeof(IfcExporterCoveringCommonPropertySetTests)
            .GetField(fixture, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!);

    private static string Resolve(Element material) => IfcExporter.ResolveMaterialName(1, null,
        new Dictionary<int, Element> { [1] = material });

    private static Element Material() => new() { Id = 1, TypeId = 822, Name = "Legacy name",
        CategoryId = -2000700, SerializedMaterialName = "Own authored name" };

    private static Parameter Text(string name, string value) => new() {
        Definition = new Definition { Name = name }, StorageType = StorageType.String, Value = value };
}
