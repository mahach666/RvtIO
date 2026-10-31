using System.Text.Json;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CurtainFamilyHostSerializationTests
{
    [Theory]
    [InlineData(897, -2000170)]
    [InlineData(897, -2000014)]
    [InlineData(897, -2000023)]
    [InlineData(2518, -2000171)]
    public void OwnReference_IsSeparateFromTheCuttingHost(int referenceClass, int category)
    {
        byte[] block = Block((ushort)referenceClass);
        var validation = Decode(block);
        Assert.Equal(new TfBlockDeserializer.CurtainFamilyHost(200, (ushort)referenceClass), validation);
        var state = new ParsingState();
        RvtFileReader.RecordCurtainFamilyHost(state, block, 0, Size(block), 1794, 100);
        var element = new Element { Id = 100, CategoryId = category, SerializedHostId = -1, HostId = 999 };
        ElementBuilder.ApplyCurtainFamilyHosts([element], state);
        Assert.Equal(200, element.SerializedCurtainHostId);
        Assert.Equal(-1, element.SerializedHostId);
        Assert.Equal(999, element.HostId);
        Assert.DoesNotContain("SerializedCurtainHostId", JsonSerializer.Serialize(element));
    }

    [Theory]
    [InlineData("footer")] [InlineData("header_size")] [InlineData("class")]
    [InlineData("owner")] [InlineData("zero_owner")] [InlineData("truncated_root")]
    [InlineData("truncated_reference")] [InlineData("host_zero")] [InlineData("host_negative")]
    [InlineData("self_host")] [InlineData("driver")] [InlineData("replaced_panel")]
    [InlineData("suspended")] [InlineData("multiple_refs")] [InlineData("external_pointer")]
    [InlineData("null_pointer")] [InlineData("wrong_reference")] [InlineData("unrelated_pointer")]
    [InlineData("broken_prior_store")]
    public void IncompleteAmbiguousOrUnsupportedReference_FailsClosed(string mutation)
    {
        byte[] block = Block(mutation == "suspended" ? (ushort)2518 : (ushort)897, mutation);
        if (mutation == "footer") block[^1] ^= 1;
        if (mutation == "header_size") BitConverter.GetBytes(Size(block) - 1).CopyTo(block, 8);
        if (mutation == "class") BitConverter.GetBytes((ushort)3524).CopyTo(block, 12);
        if (mutation == "owner") BitConverter.GetBytes(101u).CopyTo(block, 0);
        if (mutation == "zero_owner") BitConverter.GetBytes(0u).CopyTo(block, 0);
        Assert.Null(Decode(block));
    }

    [Fact]
    public void BoundsAreCheckedWithoutOverflow()
    {
        byte[] block = Block(897);
        Assert.Null(TfBlockDeserializer.ExtractCurtainFamilyHost(block, -1, Size(block), 1794));
        Assert.Null(TfBlockDeserializer.ExtractCurtainFamilyHost(block, 0, uint.MaxValue, 1794));
        Assert.Null(TfBlockDeserializer.ExtractCurtainFamilyHost(block, int.MaxValue, Size(block), 1794));
    }

    [Fact]
    public void UnrelatedUnsupportedTailAfterCompleteReference_DoesNotInvalidateOwnLink()
    {
        Assert.NotNull(Decode(Block(897, "bad_later_geometry")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DuplicateOrMalformedRoot_CannotBeRepairedByLaterSuccess(bool malformedFirst)
    {
        var state = new ParsingState(); byte[] good = Block(897), bad = Block(897, "null_pointer");
        byte[] first = malformedFirst ? bad : good;
        RvtFileReader.RecordCurtainFamilyHost(state, first, 0, Size(first), 1794, 100);
        RvtFileReader.RecordCurtainFamilyHost(state, good, 0, Size(good), 1794, 100);
        Assert.Equal(2, state.CurtainFamilyHostSource[100].BlockCount);
        Assert.Null(state.CurtainFamilyHostSource[100].Host);
    }

    [Theory]
    [InlineData("category")] [InlineData("reference_class")] [InlineData("duplicate")]
    [InlineData("missing")] [InlineData("wrong_owner")] [InlineData("self_host")]
    public void Builder_ClearsStaleMembershipWhenSourceDoesNotQualify(string mutation)
    {
        var state = new ParsingState();
        state.CurtainFamilyHostSource[100] = (1, new(200, 897));
        var element = new Element { Id = 100, CategoryId = -2000170, SerializedCurtainHostId = 200 };
        switch (mutation)
        {
            case "category": element.CategoryId = -2000340; break;
            case "reference_class": state.CurtainFamilyHostSource[100] = (1, new(200, 2518)); break;
            case "duplicate": state.CurtainFamilyHostSource[100] = (2, new(200, 897)); break;
            case "missing": state.CurtainFamilyHostSource.Clear(); break;
            case "wrong_owner": element.Id = 101; break;
            case "self_host": state.CurtainFamilyHostSource[100] = (1, new(100, 897)); break;
        }
        ElementBuilder.ApplyCurtainFamilyHosts([element], state);
        Assert.Null(element.SerializedCurtainHostId);
    }

    [Fact]
    public void WalkerOwnerMustEqualTheRootOwner()
    {
        var state = new ParsingState(); byte[] block = Block(897);
        RvtFileReader.RecordCurtainFamilyHost(state, block, 0, Size(block), 1794, 101);
        Assert.Null(state.CurtainFamilyHostSource[101].Host);
    }

    [Fact]
    public void OwnedHostPanelRefData_ConfirmsVisibleWallReplacement()
    {
        var validation = Decode(Block(897, "replacement"));
        Assert.Equal(new TfBlockDeserializer.CurtainFamilyHost(200, 897, 300), validation);
        var state = new ParsingState(); state.CurtainFamilyHostSource[100] = (1, validation);
        var placeholder = new Element { Id = 100, CategoryId = -2000170 };
        var wall = new Element { Id = 300, CategoryId = -2000011 };
        ElementBuilder.ApplyCurtainFamilyHosts([placeholder, wall], state);
        Assert.Null(placeholder.SerializedCurtainHostId);
        Assert.Equal(200, wall.SerializedCurtainHostId);
    }

    [Theory]
    [InlineData("replacement_zero")] [InlineData("replacement_self")]
    [InlineData("replacement_host")] [InlineData("replacement_truncated")]
    [InlineData("replacement_external")] [InlineData("replacement_wrong_class")]
    [InlineData("replacement_bad_prior_tail")]
    public void InvalidReplacementOrUnresolvedOwnedObject_FailsClosed(string mutation)
    {
        Assert.Null(Decode(Block(897, mutation)));
    }

    [Theory]
    [InlineData("two_placeholders")] [InlineData("duplicate_target")]
    [InlineData("duplicate_placeholder")] [InlineData("wrong_target_category")]
    [InlineData("wrong_placeholder_category")] [InlineData("target_block_only")]
    [InlineData("placeholder_block_only")] [InlineData("missing_placeholder")]
    [InlineData("malformed_owner")]
    public void ReplacementBuilder_RequiresUniqueOwnPlaceholderAndWall(string mutation)
    {
        var state = new ParsingState(); state.CurtainFamilyHostSource[100] = (1, new(200, 897, 300));
        var placeholder = new Element { Id = 100, CategoryId = -2000170 };
        var wall = new Element { Id = 300, CategoryId = -2000011, SerializedCurtainHostId = 200 };
        var all = new List<Element> { placeholder, wall };
        switch (mutation)
        {
            case "two_placeholders": state.CurtainFamilyHostSource[101] = (1, new(200, 897, 300)); all.Add(new Element { Id=101, CategoryId=-2000170 }); break;
            case "duplicate_target": all.Add(wall); break;
            case "duplicate_placeholder": all.Add(placeholder); break;
            case "wrong_target_category": wall.CategoryId = -2000032; break;
            case "wrong_placeholder_category": placeholder.CategoryId = -2000014; break;
            case "target_block_only": wall.IsBlockOnly = true; break;
            case "placeholder_block_only": placeholder.IsBlockOnly = true; break;
            case "missing_placeholder": all.Remove(placeholder); break;
            case "malformed_owner": state.CurtainFamilyHostSource[100] = (2, new(200, 897, 300)); break;
        }
        ElementBuilder.ApplyCurtainFamilyHosts(all, state);
        Assert.Null(wall.SerializedCurtainHostId);
    }

    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);
    private static TfBlockDeserializer.CurtainFamilyHost? Decode(byte[] block) =>
        TfBlockDeserializer.ExtractCurtainFamilyHost(block, 0, Size(block), 1794);

    private static byte[] Block(ushort referenceClass, string? mutation = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(100u); writer.Write(0u); writer.Write(0u); writer.Write((ushort)1794);
        var deferred = new List<ushort>();
        foreach (var field in TfBlockDeserializer.SerializationFields(1794)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(100); continue; }
            if (field.Name == "m_pParamValueSetInt" && mutation == "broken_prior_store")
            { writer.Write(uint.MaxValue); writer.Write((ushort)0xB49); deferred.Add(0xB49); continue; }
            if (field.Name == "m_refs")
            {
                writer.Write(mutation == "multiple_refs" ? 2u : 1u);
                if (mutation is "null_pointer" or "unrelated_pointer") writer.Write(0u);
                else
                {
                    ushort cls = mutation == "external_pointer" ? (ushort)0 : mutation == "wrong_reference" ? (ushort)731 : referenceClass;
                    writer.Write(uint.MaxValue); writer.Write(cls); if (cls > 0) deferred.Add(cls);
                }
                if (mutation == "multiple_refs") { writer.Write(uint.MaxValue); writer.Write(referenceClass); deferred.Add(referenceClass); }
                if (mutation == "truncated_root") break;
                continue;
            }
            if (field.Name == "m_oFamInstSpec" && mutation == "unrelated_pointer")
            { writer.Write(uint.MaxValue); writer.Write(referenceClass); deferred.Add(referenceClass); continue; }
            if (field.Name == "m_pConnectorManager" && mutation is "bad_later_geometry" or "replacement_bad_prior_tail")
            { writer.Write(uint.MaxValue); writer.Write((ushort)1589); deferred.Add(1589); continue; }
            WriteDefault(field);
        }
        for (int index = 0; index < deferred.Count; index++)
        {
            ushort cls = deferred[index];
            if (mutation == "truncated_root") break;
            if (cls == 0xB49) { writer.Write(uint.MaxValue); break; }
            if (cls == 1589) { writer.Write((byte)255); break; }
            foreach (var field in TfBlockDeserializer.SerializationFields(cls)!)
            {
                if (field.Name == "m_hostId")
                {
                    writer.Write(mutation switch { "host_zero" => 0, "host_negative" => -1, "self_host" => 100, _ => 200 });
                    if (mutation == "truncated_reference") break;
                }
                else if (field.Name == "m_driverId") writer.Write(mutation == "driver" ? 0 : -1);
                else if (field.Name == "m_hostPanelRefData")
                {
                    if (mutation?.StartsWith("replacement") == true)
                    {
                        ushort replacementClass = mutation == "replacement_external" ? (ushort)0 : mutation == "replacement_wrong_class" ? (ushort)2174 : (ushort)2175;
                        writer.Write(uint.MaxValue); writer.Write(replacementClass);
                        if (replacementClass > 0) deferred.Add(replacementClass);
                    }
                    else writer.Write(mutation == "replaced_panel" ? 1u : 0u);
                }
                else if (field.Name == "m_panelHostObjId")
                {
                    if (mutation != "replacement_truncated")
                        writer.Write(mutation switch { "replacement_zero" => 0, "replacement_self" => 100, "replacement_host" => 200, _ => 300 });
                }
                else if (field.Name == "m_wasSuspendedOnFail") writer.Write(mutation == "suspended" ? (byte)1 : (byte)0);
                else WriteDefault(field);
            }
        }
        writer.Write(0u);
        byte[] block = stream.ToArray(); uint size = (uint)block.Length - 16;
        BitConverter.GetBytes(size).CopyTo(block, 8); BitConverter.GetBytes(size).CopyTo(block, block.Length - 4);
        return block;

        void WriteDefault(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 == 5) { writer.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int index = 0; index < count; index++)
                switch (field.Type)
                {
                    case 1: case 2: writer.Write((byte)0); break;
                    case 4: writer.Write(0); break;
                    case 7: writer.Write(0d); break;
                    case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                        foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) WriteDefault(nested);
                        break;
                    case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                    default: throw new InvalidOperationException(field.Name);
                }
        }
    }
}
