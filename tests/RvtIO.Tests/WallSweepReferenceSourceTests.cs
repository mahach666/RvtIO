using System.Text;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallSweepReferenceSourceTests
{
    [Fact]
    public void OwnSweepAndCorniceName_DoNotBorrowThePublicTypeOrMark()
    {
        var state = State();
        var instance = new Element { Id = 100, CategoryId = -2000181, Name = "Occurrence mark", TypeId = 999 };
        var type = new Element { Id = 500, CategoryId = -2000181, Name = "Type mark" };
        ElementBuilder.ApplyValidatedWallSweepTypeReferenceNames([instance, type], state);
        Assert.Equal("Own cornice", instance.ValidatedProxyTypeReferenceName);
        Assert.False(instance.ValidatedProxyTypeReferenceIsExplicit);
        Assert.Equal(999, instance.TypeId);
        Assert.Equal("Occurrence mark", instance.Name);
        Assert.Equal("Type mark", type.Name);
    }

    [Theory]
    [InlineData("footer")] [InlineData("header_size")] [InlineData("class")]
    [InlineData("owner")] [InlineData("zero_owner")] [InlineData("truncated_root")]
    public void InvalidOwningRoot_FailsClosed(string mutation)
    {
        foreach (ushort cls in new ushort[] { 4157, 873 })
        {
            byte[] block = Block(cls, mutation);
            if (mutation == "footer") block[^1] ^= 1;
            if (mutation == "header_size") block[8] ^= 1;
            if (mutation == "class") block[12] ^= 1;
            if (mutation == "owner") block[0] ^= 1;
            if (mutation == "zero_owner") BitConverter.GetBytes(0).CopyTo(block, 0);
            if (cls == 4157) Assert.Null(TfBlockDeserializer.ExtractWallSweepTypeId(block, 0, Size(block), cls));
            else Assert.Null(TfBlockDeserializer.ExtractCorniceTypeName(block, 0, Size(block), cls));
        }
    }

    [Theory]
    [InlineData("zero_type")] [InlineData("self_type")] [InlineData("negative_type")]
    public void InvalidSweepTypeId_IsNotATypeLink(string mutation)
    {
        byte[] block = Block(4157, mutation);
        Assert.Null(TfBlockDeserializer.ExtractWallSweepTypeId(block, 0, Size(block), 4157));
    }

    [Theory]
    [InlineData("null_pointer")] [InlineData("external_pointer")]
    [InlineData("wrong_pointer")] [InlineData("bad_prior_store")]
    [InlineData("truncated_name")] [InlineData("invalid_utf16")]
    public void MissingOrMalformedOwnName_DoesNotBecomeTheMark(string mutation)
    {
        byte[] block = Block(873, mutation);
        Assert.Null(TfBlockDeserializer.ExtractCorniceTypeName(block, 0, Size(block), 873));
    }

    [Fact]
    public void BoundsAreCheckedBeforeReading()
    {
        byte[] sweep = Block(4157), type = Block(873);
        Assert.Null(TfBlockDeserializer.ExtractWallSweepTypeId(sweep, -1, Size(sweep), 4157));
        Assert.Null(TfBlockDeserializer.ExtractWallSweepTypeId(sweep, 0, uint.MaxValue, 4157));
        Assert.Null(TfBlockDeserializer.ExtractCorniceTypeName(type, int.MaxValue, Size(type), 873));
        Assert.Null(TfBlockDeserializer.ExtractCorniceTypeName(type, 0, uint.MaxValue, 873));
        Assert.Null(TfBlockDeserializer.ExtractCorniceTypeName(type, 0, Size(type), 871));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WalkerOwnerMustMatchTheHeader(bool type)
    {
        ushort cls = type ? (ushort)873 : (ushort)4157;
        byte[] block = Block(cls); var state = new ParsingState();
        RvtFileReader.RecordWallSweepReferenceSource(state, block, 0, Size(block), cls, 999);
        if (type) Assert.Null(state.CorniceTypeNames[999].Name);
        else Assert.Null(state.WallSweepTypeIds[999].TypeId);
    }

    [Theory]
    [InlineData("duplicate_sweep")] [InlineData("duplicate_type")]
    [InlineData("mixed_host_root")] [InlineData("mixed_symbol_root")]
    [InlineData("malformed_sweep_first")] [InlineData("malformed_type_first")]
    public void DuplicateOrConflictingRoots_CannotBeRepaired(string mode)
    {
        var state = new ParsingState();
        byte[] sweep = Block(4157), type = Block(873);
        if (mode == "malformed_sweep_first") Record(state, Block(4157, "truncated_root"));
        if (mode == "malformed_type_first") Record(state, Block(873, "null_pointer"));
        Record(state, sweep); Record(state, type);
        if (mode == "duplicate_sweep") Record(state, sweep);
        if (mode == "duplicate_type") Record(state, type);
        if (mode == "mixed_host_root") RvtFileReader.RecordWallSweepReferenceSource(state, sweep, 0, Size(sweep), 3524, 100);
        if (mode == "mixed_symbol_root") RvtFileReader.RecordWallSweepReferenceSource(state, type, 0, Size(type), 512, 500);
        var instance = new Element { Id = 100, CategoryId = -2000181 };
        ElementBuilder.ApplyValidatedWallSweepTypeReferenceNames([instance, new Element { Id=500, CategoryId=-2000181 }], state);
        Assert.Null(instance.ValidatedProxyTypeReferenceName);
    }

    [Theory]
    [InlineData("missing_instance")] [InlineData("missing_type")]
    [InlineData("duplicate_instance")] [InlineData("duplicate_type")]
    [InlineData("wrong_instance_category")] [InlineData("wrong_type_category")]
    public void MaterializedOwnerAndTypeMustBeUniqueAndInTheOwnCategory(string mode)
    {
        var state = State();
        var instance = new Element { Id = 100, CategoryId = -2000181 };
        var type = new Element { Id = 500, CategoryId = -2000181 };
        var all = new List<Element> { instance, type };
        if (mode == "missing_instance") all.Remove(instance);
        if (mode == "missing_type") all.Remove(type);
        if (mode == "duplicate_instance") all.Add(instance);
        if (mode == "duplicate_type") all.Add(type);
        if (mode == "wrong_instance_category") instance.CategoryId = -2000011;
        if (mode == "wrong_type_category") type.CategoryId = -2000011;
        ElementBuilder.ApplyValidatedWallSweepTypeReferenceNames(all, state);
        Assert.Null(instance.ValidatedProxyTypeReferenceName);
    }

    [Theory]
    [InlineData("instance", "Occurrence")] [InlineData("type", "Type")]
    [InlineData("type_alias", "Type alias")] [InlineData("expression", null)]
    [InlineData("conflict", null)]
    public void ExplicitReferencePrecedenceIsPreserved(string mode, string? expected)
    {
        var instance = new Element { Id = 100, CategoryId = -2000181 };
        var type = new Element { Id = 500, CategoryId = -2000181 };
        var scope = mode.StartsWith("type") ? type : instance;
        scope.Parameters.Add(new Parameter { Definition = new Definition { Name = mode == "type_alias" ? "Reference[Type]" : "Reference" }, StorageType = StorageType.String,
            Value = mode switch { "instance" => "Occurrence", "type" => "Type", "type_alias" => "Type alias", "expression" => "{Name}", _ => "One" } });
        if (mode == "conflict") scope.Parameters.Add(new Parameter { Definition = new Definition { Name = "Reference" }, StorageType = StorageType.String, Value = "Two" });
        ElementBuilder.ApplyValidatedWallSweepTypeReferenceNames([instance,type], State());
        Assert.Equal(expected, instance.ValidatedProxyTypeReferenceName);
        Assert.Equal(expected == null, instance.ValidatedProxyTypeReferenceIsBlocked);
        Assert.Equal(expected != null, instance.ValidatedProxyTypeReferenceIsExplicit);
    }

    private static ParsingState State()
    {
        var state = new ParsingState(); Record(state, Block(4157)); Record(state, Block(873)); return state;
    }
    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);
    private static void Record(ParsingState state, byte[] block) => RvtFileReader.RecordWallSweepReferenceSource(
        state, block, 0, Size(block), BitConverter.ToUInt16(block,12), BitConverter.ToUInt32(block,0));

    private static byte[] Block(ushort cls, string? mutation = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        int owner = cls == 4157 ? 100 : 500;
        writer.Write(owner); writer.Write(0u); writer.Write(0u); writer.Write(cls);
        var deferred = new List<ushort>();
        foreach (var field in TfBlockDeserializer.SerializationFields(cls)!)
        {
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(owner); continue; }
            if (field.Declaring == 4157 && field.Name == "m_typeId")
            { writer.Write(mutation switch { "zero_type" => 0, "negative_type" => -1, "self_type" => owner, _ => 500 }); continue; }
            if (field.Name == "m_pParamValueSetAString" && cls == 873) { Pointer(2886); continue; }
            if (field.Name == "m_symbolInfo")
            {
                if (mutation == "null_pointer") writer.Write(0u);
                else Pointer(mutation == "external_pointer" ? (ushort)0 : mutation == "wrong_pointer" ? (ushort)3904 : (ushort)3905);
                continue;
            }
            if (mutation == "truncated_root" && field.Name == (cls == 4157 ? "m_notFinal" : "m_cutsWall")) break;
            WriteDefault(field);
        }
        if (mutation != "truncated_root") foreach (ushort deferredClass in deferred)
        {
            if (deferredClass == 2886)
            {
                writer.Write(mutation == "bad_prior_store" ? uint.MaxValue : 1u);
                if (mutation == "bad_prior_store") break;
                writer.Write(11070); WriteString("Wrong mark");
            }
            else if (deferredClass == 3905)
            {
                if (mutation == "truncated_name") writer.Write(999u);
                else if (mutation == "invalid_utf16") { writer.Write(1u); writer.Write((ushort)0xD800); }
                else WriteString("Own cornice");
            }
        }
        writer.Write(0u);
        byte[] block = stream.ToArray(); uint size = (uint)block.Length - 16;
        BitConverter.GetBytes(size).CopyTo(block, 8); BitConverter.GetBytes(size).CopyTo(block, block.Length - 4);
        return block;
        void Pointer(ushort id) { writer.Write(uint.MaxValue); writer.Write(id); if (id > 0) deferred.Add(id); }
        void WriteString(string value) { writer.Write((uint)value.Length); writer.Write(Encoding.Unicode.GetBytes(value)); }
        void WriteDefault(TfBlockDeserializer.TfField field)
        {
            if (field.Mods >> 4 == 5) { writer.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int index = 0; index < count; index++) switch (field.Type)
            {
                case 1: case 2: writer.Write((byte)0); break;
                case 4: writer.Write(0); break;
                case 7: writer.Write(0d); break;
                case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                    foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) WriteDefault(nested); break;
                case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(field.Name);
            }
        }
    }
}
