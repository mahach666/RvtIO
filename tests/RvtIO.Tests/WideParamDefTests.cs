using System.Text;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The parameter definition of a 64-bit-id file (ParamElem with its owned ParamDef) as <see cref="WideNativeParams"/> hands it to
/// <see cref="UserParameterCatalog"/>: the group as a ForgeTypeId string (Revit 2024 and later) or as the number of the older layouts,
/// the caption, the type id and the class of the owned definition. Hand-made blocks of a schema with the class and field names of the files.</summary>
public class WideParamDefTests
{
    private const int IdentifierId = 1, ElementIdId = 2, ForgeId = 3, ElemId = 4, ParamElemId = 5, ParamDefId = 6, ParamDefStringId = 7;

    private static SchemaField Field(string name, byte type, byte mods = 0, int? reference = null) =>
        new(name, type, mods, 0, null, reference, null, null);

    private static SchemaDocument Schema(bool groupAsString) => new()
    {
        Classes =
        [
            new(IdentifierId, "Identifier", 0, 2, [Field("m_id64", 11)], []),
            new(ElementIdId, "ElementId", 0, 1, [Field("m_id", 14, 0, IdentifierId)], []),
            new(ForgeId, "ForgeTypeId", 0, 1, [Field("m_typeId", 8, 96)], []),
            new(ElemId, "Element", 0, 1, [], []),
            new(ParamElemId, "ParamElem", ElemId, 1, [Field("m_description", 8, 96), Field("m_pParamDef", 14, 1, ParamDefId)], []),
            new(ParamDefId, "ParamDef", 0, 1,
                groupAsString
                    ? [Field("m_groupTypeId", 14, 0, ForgeId), Field("m_caption", 8, 96), Field("m_typeId", 14, 0, ForgeId), Field("m_paramElemId", 14, 0, ElementIdId)]
                    : [Field("m_caption", 8, 96), Field("m_groupElemId", 14, 0, ElementIdId), Field("m_paramElemId", 14, 0, ElementIdId)], []),
            new(ParamDefStringId, "ParamDefString", ParamDefId, 1, [], []),
        ],
    };

    private static void Text(BinaryWriter w, string s) { w.Write((uint)s.Length); w.Write(Encoding.Unicode.GetBytes(s)); }

    private static TfBlockDeserializer.NativeParams Read(bool groupAsString)
    {
        var stream = new MemoryStream();
        var w = new BinaryWriter(stream, Encoding.Unicode);
        w.Write((ushort)ParamElemId);
        Text(w, "");                              // m_description
        w.Write(1u); w.Write((ushort)ParamDefStringId);   // m_pParamDef: handle and class tag
        if (groupAsString) { Text(w, "autodesk.parameter.group:dimensions-1.0.0"); Text(w, "Height"); Text(w, "revit.local.family:ab-1.0.0"); }
        else { Text(w, "Height"); w.Write(-5000101L); }
        w.Write(4321L);                           // m_paramElemId
        byte[] block = stream.ToArray();
        var fields = new WideNativeParams(Schema(groupAsString));
        var walk = fields.Walker.Run(block, 0, block.Length, ParamElemId);
        Assert.True(walk.Exact, walk.Error);
        return fields.Build(walk)!;
    }

    [Fact]
    public void AGroupTypeIdIsKeptAsText_AndTheOwnedDefinitionClassAsTheRevit2022Number()
    {
        var np = Read(true);
        Assert.Equal("autodesk.parameter.group:dimensions-1.0.0", np.StringFields[WideNativeParams.GroupTypeKey]);
        Assert.Equal("Height", np.StringFields["301:m_caption"]);
        Assert.Equal("revit.local.family:ab-1.0.0", np.StringFields["301:m_typeId"]);
        Assert.Equal(4321, np.Fields["301:m_paramElemId"]);
        Assert.Equal(606, np.Fields["2870:m_pParamDef.class"]);     // ParamDefString
        Assert.False(np.Fields.ContainsKey("301:m_groupElemId"));
    }

    [Fact]
    public void ANumberGroupKeepsItsKey()
    {
        var np = Read(false);
        Assert.Equal(-5000101, np.Fields["301:m_groupElemId"]);
        Assert.False(np.StringFields.ContainsKey(WideNativeParams.GroupTypeKey));
        Assert.Equal(606, np.Fields["2870:m_pParamDef.class"]);
    }
}
