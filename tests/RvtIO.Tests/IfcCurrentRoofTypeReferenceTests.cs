using System.Text.Json;
using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class IfcCurrentRoofTypeReferenceTests
{
    private static Element RoofType() => new() { Id = 9792925, CategoryId = -2000035,
        SerializedSystemTypeClassId = 3452, Name = "Alias", FamilyName = "Borrowed family",
        ValidatedIfcTypeObjectName = "Borrowed family:Alias", ValidatedIfcTypeOwnName = "Alias",
        ValidatedSystemFamilyName = "Native family", ValidatedHostTypeLayerSetName = "Native family:Own type:variant" };

    [Fact]
    public void OwnRoofSymbolNameFeedsReferenceWithoutChangingIdentity()
    {
        var type = RoofType(); string before = JsonSerializer.Serialize(type);
        Assert.Equal("Own type:variant", IfcExporter.ResolveTypeReferenceValueCurrent(type));
        Assert.Equal(before, JsonSerializer.Serialize(type));
        Assert.Equal("Borrowed family:Alias", IfcExporter.TypeObjectNameParts(type, true).Full);
    }

    [Theory]
    [InlineData("wrong_category")] [InlineData("wrong_class")] [InlineData("block_only")]
    [InlineData("missing_family")] [InlineData("missing_name")] [InlineData("wrong_prefix")] [InlineData("empty_name")]
    public void MissingOwnRoofValidationKeepsTheExistingReference(string mutation)
    {
        var type = RoofType();
        switch (mutation)
        {
            case "wrong_category": type.CategoryId = -2000032; break;
            case "wrong_class": type.SerializedSystemTypeClassId = 1915; break;
            case "block_only": type.IsBlockOnly = true; break;
            case "missing_family": type.ValidatedSystemFamilyName = null; break;
            case "missing_name": type.ValidatedHostTypeLayerSetName = null; break;
            case "wrong_prefix": type.ValidatedHostTypeLayerSetName = "Other family:Own type"; break;
            case "empty_name": type.ValidatedHostTypeLayerSetName = "Native family:"; break;
        }
        Assert.Equal("Alias", IfcExporter.ResolveTypeReferenceValueCurrent(type));
    }

    [Fact]
    public void ExplicitReferencePrecedesOwnRoofName()
    {
        var type = RoofType();
        type.Parameters.Add(new Parameter { Id = -1, StorageType = StorageType.String,
            Definition = new Definition { Name = "Reference" }, Value = "Explicit" });
        Assert.Equal("Explicit", IfcExporter.ResolveTypeReferenceValueCurrent(type));
    }

    [SkippableTheory]
    [InlineData(false)] [InlineData(true)]
    public void SampleRoofRootProvidesExactNameAndDuplicateRootsBlockIt(bool duplicate)
    {
        using var stream = TestFiles.OpenBlock("sample-a-roof-type-9792925.bin");
        using var memory = new MemoryStream(); stream.CopyTo(memory); byte[] bytes = memory.ToArray();
        var state = new ParsingState(); state.SystemTypeRoots[9792925] = (1, 3452, null);
        RvtFileReader.RecordHostTypeCompoundStructure(state, bytes, 0, BitConverter.ToUInt32(bytes, 8), 3452, 9792925);
        if (duplicate) RvtFileReader.RecordHostTypeCompoundStructure(state, bytes, 0, BitConverter.ToUInt32(bytes, 8), 3452, 9792925);
        var type = RoofType();
        Assert.Equal(duplicate ? 0 : 1, ElementBuilder.ApplyHostTypeCompoundStructures([type], state));
        string reference = IfcExporter.ResolveTypeReferenceValueCurrent(type);
        if (duplicate) { Assert.Equal("Alias", reference); return; }
        // The exact text comes from a private project file; it is compared by SHA-256 so that the name stays out of the sources.
        Assert.Equal(ExpectedReferenceSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(reference))));
    }

    private const string ExpectedReferenceSha256 = "7F7B723F0B16592C11275649236C5C6DF01913B24467E2C683AD2CA9E3A73141";
}
