using System.Text;
using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class RoofOrRailingReferenceSourceTests
{
    [Fact]
    public void OwnSweepAndRoofOrRailingName_DoNotBorrowThePublicTypeOrMark()
    {
        var state = State();
        var instance = new Element { Id = 100, CategoryId = -2000035, Name = "Occurrence mark", TypeId = 999 };
        var type = new Element { Id = 500, CategoryId = -2000035, Name = "Type mark" };
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance, type], state);
        Assert.Equal("Own system type", instance.ValidatedRoofOrRailingTypeReferenceName);
        Assert.False(instance.ValidatedRoofOrRailingTypeReferenceIsExplicit);
        Assert.Equal(999, instance.TypeId);
        Assert.Equal("Occurrence mark", instance.Name);
        Assert.Equal("Type mark", type.Name);
    }

    [Theory]
    [InlineData("footer")] [InlineData("header_size")] [InlineData("class")]
    [InlineData("owner")] [InlineData("zero_owner")] [InlineData("truncated_root")]
    public void InvalidOwningRoot_FailsClosed(string mutation)
    {
        foreach (ushort cls in new ushort[] { 3451, 3054, 485, 3452, 3705 })
        {
            byte[] block = Block(cls, mutation);
            if (mutation == "footer") block[^1] ^= 1;
            if (mutation == "header_size") block[8] ^= 1;
            if (mutation == "class") block[12] ^= 1;
            if (mutation == "owner") block[0] ^= 1;
            if (mutation == "zero_owner") BitConverter.GetBytes(0).CopyTo(block, 0);
            if (cls is 3451 or 3054 or 485) Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeId(block, 0, Size(block), cls));
            else Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeName(block, 0, Size(block), cls));
        }
    }

    [Theory]
    [InlineData("zero_type")] [InlineData("self_type")] [InlineData("negative_type")]
    public void InvalidSweepTypeId_IsNotATypeLink(string mutation)
    {
        byte[] block = Block(3451, mutation);
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeId(block, 0, Size(block), 3451));
    }

    [Theory]
    [InlineData("null_pointer")] [InlineData("external_pointer")]
    [InlineData("wrong_pointer")] [InlineData("bad_prior_store")]
    [InlineData("truncated_name")] [InlineData("invalid_utf16")]
    public void MissingOrMalformedOwnName_DoesNotBecomeTheMark(string mutation)
    {
        byte[] block = Block(3452, mutation);
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeName(block, 0, Size(block), 3452));
    }

    [Fact]
    public void BoundsAreCheckedBeforeReading()
    {
        byte[] sweep = Block(3451), type = Block(3452);
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeId(sweep, -1, Size(sweep), 3451));
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeId(sweep, 0, uint.MaxValue, 3451));
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeName(type, int.MaxValue, Size(type), 3452));
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeName(type, 0, uint.MaxValue, 3452));
        Assert.Null(TfBlockDeserializer.ExtractRoofOrRailingTypeName(type, 0, Size(type), 871));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WalkerOwnerMustMatchTheHeader(bool type)
    {
        ushort cls = type ? (ushort)3452 : (ushort)3451;
        byte[] block = Block(cls); var state = new ParsingState();
        RvtFileReader.RecordRoofOrRailingReferenceSource(state, block, 0, Size(block), cls, 999);
        if (type) Assert.Null(state.RoofOrRailingTypeNames[999].Name);
        else Assert.Null(state.RoofOrRailingTypeIds[999].TypeId);
    }

    [Theory]
    [InlineData("duplicate_sweep")] [InlineData("duplicate_type")]
    [InlineData("mixed_host_root")] [InlineData("mixed_symbol_root")]
    [InlineData("malformed_sweep_first")] [InlineData("malformed_type_first")]
    public void DuplicateOrConflictingRoots_CannotBeRepaired(string mode)
    {
        var state = new ParsingState();
        byte[] sweep = Block(3451), type = Block(3452);
        if (mode == "malformed_sweep_first") Record(state, Block(3451, "truncated_root"));
        if (mode == "malformed_type_first") Record(state, Block(3452, "null_pointer"));
        Record(state, sweep); Record(state, type);
        if (mode == "duplicate_sweep") Record(state, sweep);
        if (mode == "duplicate_type") Record(state, type);
        if (mode == "mixed_host_root") RvtFileReader.RecordRoofOrRailingReferenceSource(state, sweep, 0, Size(sweep), 3524, 100);
        if (mode == "mixed_symbol_root") RvtFileReader.RecordRoofOrRailingReferenceSource(state, type, 0, Size(type), 512, 500);
        var instance = new Element { Id = 100, CategoryId = -2000035 };
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance, new Element { Id=500, CategoryId=-2000035 }], state);
        Assert.Null(instance.ValidatedRoofOrRailingTypeReferenceName);
    }

    [Theory]
    [InlineData("missing_instance")] [InlineData("missing_type")]
    [InlineData("duplicate_instance")] [InlineData("duplicate_type")]
    [InlineData("wrong_instance_category")] [InlineData("wrong_type_category")]
    public void MaterializedOwnerAndTypeMustBeUniqueAndInTheOwnCategory(string mode)
    {
        var state = State();
        var instance = new Element { Id = 100, CategoryId = -2000035 };
        var type = new Element { Id = 500, CategoryId = -2000035 };
        var all = new List<Element> { instance, type };
        if (mode == "missing_instance") all.Remove(instance);
        if (mode == "missing_type") all.Remove(type);
        if (mode == "duplicate_instance") all.Add(instance);
        if (mode == "duplicate_type") all.Add(type);
        if (mode == "wrong_instance_category") instance.CategoryId = -2000011;
        if (mode == "wrong_type_category") type.CategoryId = -2000011;
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames(all, state);
        Assert.Null(instance.ValidatedRoofOrRailingTypeReferenceName);
    }

    [Theory]
    [InlineData("instance", "Occurrence")] [InlineData("type", "Type")]
    [InlineData("type_alias", "Type alias")] [InlineData("expression", null)]
    [InlineData("conflict", null)]
    public void ExplicitReferencePrecedenceIsPreserved(string mode, string? expected)
    {
        var instance = new Element { Id = 100, CategoryId = -2000035 };
        var type = new Element { Id = 500, CategoryId = -2000035 };
        var scope = mode.StartsWith("type") ? type : instance;
        scope.Parameters.Add(new Parameter { Definition = new Definition { Name = mode == "type_alias" ? "Reference[Type]" : "Reference" }, StorageType = StorageType.String,
            Value = mode switch { "instance" => "Occurrence", "type" => "Type", "type_alias" => "Type alias", "expression" => "{Name}", _ => "One" } });
        if (mode == "conflict") scope.Parameters.Add(new Parameter { Definition = new Definition { Name = "Reference" }, StorageType = StorageType.String, Value = "Two" });
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance,type], State());
        Assert.Equal(expected, instance.ValidatedRoofOrRailingTypeReferenceName);
        Assert.Equal(expected == null, instance.ValidatedRoofOrRailingTypeReferenceIsBlocked);
        Assert.Equal(expected != null, instance.ValidatedRoofOrRailingTypeReferenceIsExplicit);
    }

    [Theory]
    [InlineData(3451,3452,-2000035)] [InlineData(3054,3452,-2000035)] [InlineData(485,3705,-2000126)]
    public void EachExactSourceKindUsesItsOwnType(int cls, int typeCls, int category)
    {
        var state = new ParsingState();
        Record(state, Block((ushort)cls)); Record(state, Block((ushort)typeCls));
        var instance = new Element { Id=100, CategoryId=category };
        var type = new Element { Id=500, CategoryId=category };
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance,type],state);
        Assert.Equal("Own system type", instance.ValidatedRoofOrRailingTypeReferenceName);
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance,type],new ParsingState());
        Assert.Null(instance.ValidatedRoofOrRailingTypeReferenceName);
    }

    [Fact]
    public void CorrectCategoryCannotSubstituteForTheOwnTypeClass()
    {
        var state = State();
        state.RoofOrRailingTypeNames[500] = (1,3705,"Other type");
        var instance = new Element { Id=100,CategoryId=-2000035 };
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance,new Element { Id=500,CategoryId=-2000035 }],state);
        Assert.Null(instance.ValidatedRoofOrRailingTypeReferenceName);
    }

    [Fact]
    public void RailingFamilyInstance_UsesItsOwnFamilySymbolName()
    {
        var state = FamilyRailingState();
        var instance = new Element { Id = 200, CategoryId = -2000126, Name = "Occurrence mark", TypeId = 999 };
        var symbol = new Element { Id = 600, CategoryId = -2000126, Name = "Public symbol" };
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance, symbol], state);
        Assert.Equal("Own family symbol", instance.ValidatedRoofOrRailingTypeReferenceName);
        Assert.False(instance.ValidatedRoofOrRailingTypeReferenceIsExplicit);
        Assert.False(instance.ValidatedRoofOrRailingTypeReferenceIsBlocked);
        Assert.Equal(999, instance.TypeId);
        Assert.Equal("Occurrence mark", instance.Name);
    }

    [Theory]
    [InlineData("other_category")] [InlineData("symbol_category")] [InlineData("missing_symbol")]
    [InlineData("duplicate_symbol")] [InlineData("duplicate_instance")] [InlineData("instance_blocks")]
    [InlineData("symbol_blocks")] [InlineData("no_name")] [InlineData("self_symbol")]
    [InlineData("railing_root")] [InlineData("roof_category")]
    public void RailingFamilyInstance_FailsClosed(string mode)
    {
        var state = FamilyRailingState();
        var instance = new Element { Id = 200, CategoryId = -2000126 };
        var symbol = new Element { Id = 600, CategoryId = -2000126 };
        var all = new List<Element> { instance, symbol };
        switch (mode)
        {
            case "other_category": instance.CategoryId = -2000151; symbol.CategoryId = -2000151; break;
            case "roof_category": instance.CategoryId = -2000035; symbol.CategoryId = -2000035; break;
            case "symbol_category": symbol.CategoryId = -2000151; break;
            case "missing_symbol": all.Remove(symbol); break;
            case "duplicate_symbol": all.Add(new Element { Id = 600, CategoryId = -2000126 }); break;
            case "duplicate_instance": all.Add(new Element { Id = 200, CategoryId = -2000126 }); break;
            case "instance_blocks": state.FamilyInstanceSymbolIds[200] = (2, null); break;
            case "symbol_blocks": state.FamilySymbolNames[600] = (2, null); break;
            case "no_name": state.FamilySymbolNames[600] = (1, null); break;
            case "self_symbol": state.FamilyInstanceSymbolIds[200] = (1, 200); break;
            case "railing_root": state.RoofOrRailingTypeIds[200] = (1, 485, null); break;
        }
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames(all, state);
        Assert.Null(instance.ValidatedRoofOrRailingTypeReferenceName);
        Assert.False(instance.ValidatedRoofOrRailingTypeReferenceIsBlocked);
    }

    [Fact]
    public void RailingFamilyInstance_KeepsExplicitReferencePrecedence()
    {
        var instance = new Element { Id = 200, CategoryId = -2000126 };
        var symbol = new Element { Id = 600, CategoryId = -2000126 };
        symbol.Parameters.Add(new Parameter { Definition = new Definition { Name = "Reference[Type]" },
            StorageType = StorageType.String, Value = "Explicit" });
        ElementBuilder.ApplyValidatedRoofOrRailingTypeReferenceNames([instance, symbol], FamilyRailingState());
        Assert.Equal("Explicit", instance.ValidatedRoofOrRailingTypeReferenceName);
        Assert.True(instance.ValidatedRoofOrRailingTypeReferenceIsExplicit);
    }

    private static ParsingState FamilyRailingState()
    {
        var state = new ParsingState();
        state.FamilyInstanceSymbolIds[200] = (1, 600);
        state.FamilySymbolNames[600] = (1, "Own family symbol");
        return state;
    }

    private static ParsingState State()
    {
        var state = new ParsingState(); Record(state, Block(3451)); Record(state, Block(3452)); return state;
    }
    private static uint Size(byte[] block) => BitConverter.ToUInt32(block, 8);
    private static void Record(ParsingState state, byte[] block) => RvtFileReader.RecordRoofOrRailingReferenceSource(
        state, block, 0, Size(block), BitConverter.ToUInt16(block,12), BitConverter.ToUInt32(block,0));

    private static byte[] Block(ushort cls, string? mutation = null)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        int owner = cls is 3451 or 3054 or 485 ? 100 : 500;
        writer.Write(owner); writer.Write(0u); writer.Write(0u); writer.Write(cls);
        var deferred = new List<ushort>();
        foreach (var field in TfBlockDeserializer.SerializationFields(cls)!)
        {
            if (mutation == "truncated_root" && field == TfBlockDeserializer.SerializationFields(cls)![^1]) break;
            if (field.Declaring == 37 && field.Name == "m_id") { writer.Write(owner); continue; }
            if ((field.Declaring == 1605 && field.Name == "m_roofAttributesId") || (field.Declaring == 485 && field.Name == "m_stairsRailingAttrId"))
            { writer.Write(mutation switch { "zero_type" => 0, "negative_type" => -1, "self_type" => owner, _ => 500 }); continue; }
            if (field.Name == "m_pParamValueSetAString" && cls is 3452 or 3705) { Pointer(2886); continue; }
            if (field.Name == "m_symbolInfo")
            {
                if (mutation == "null_pointer") writer.Write(0u);
                else Pointer(mutation == "external_pointer" ? (ushort)0 : mutation == "wrong_pointer" ? (ushort)3904 : (ushort)3905);
                continue;
            }
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
                else WriteString("Own system type");
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
            if (field.Declaring == 297 && field.Name == "m_minmax") { writer.Write(new byte[48]); return; }
            if (field.Mods >> 4 == 5) { writer.Write(0u); return; }
            int count = field.Mods >> 4 == 1 ? field.Cnt!.Value : 1;
            for (int index = 0; index < count; index++) switch (field.Type)
            {
                case 1: case 2: case 3: writer.Write((byte)0); break;
                case 4: case 5: case 6: writer.Write(0); break;
                case 8: writer.Write(0u); break;
                case 9: writer.Write(new byte[16]); break;
                case 10: writer.Write(0L); break;
                case 13: writer.Write(new byte[(field.Cnt ?? 3)*8]); break;
                case 7: writer.Write(0d); break;
                case 14 when (field.Mods & 15) == 0 && field.Ref is not null and not 20:
                    foreach (var nested in TfBlockDeserializer.SerializationFields(field.Ref.Value)!) WriteDefault(nested); break;
                case 14: writer.Write(field.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(field.Name);
            }
        }
    }
}
