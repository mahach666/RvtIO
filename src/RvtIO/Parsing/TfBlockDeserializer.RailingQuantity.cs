namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record OwnRailingQuantityInput(ushort ClassId, int OwnerId,
        int? SymbolId, int? RailingId, double? LengthFeet);

    internal static OwnRailingQuantityInput? ExtractOwnRailingQuantityInput(byte[] data,
        int start, uint size, ushort classId)
    {
        if (classId is not (485 or 492) || start < 0 || start > data.Length - 16 || size > int.MaxValue
            || (long)start + 16 + size > data.Length || BitConverter.ToUInt32(data, start + 8) != size
            || BitConverter.ToUInt16(data, start + 12) != classId
            || BitConverter.ToUInt32(data, start + 12 + (int)size) != size) return null;
        var fields = SerializationFields(classId);
        if (fields == null || !HasExactSchemaField(fields, 37, "m_id", 14, 0, 20)) return null;
        if (classId == 485 && !HasExactSchemaField(fields, 485, "m_baseRailingSymbolId", 14, 0, 20)
            || classId == 492 && (!HasExactSchemaField(fields, 492, "m_baseRailingId", 14, 0, 20)
                || !HasExactSchemaField(fields, 492, "m_approxLength", 7, 0, null)
                || !HasExactSchemaField(fields, 492, "m_paramsAndIds", 14, 80, null))) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = start + 12 + (int)size, RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        int? owner = null, symbol = null, railing = null; double? length = null;
        try
        {
            foreach (var field in fields)
            {
                int position = cursor.Pos;
                if (field is { Declaring: 492, Name: "m_paramsAndIds" })
                {
                    uint count = ReadU32(cursor);
                    var members = SerializationFields(493);
                    (string Name, byte Type, int? Ref)[] expected =
                    [ ("m_botAngle", 7, null), ("m_height", 7, null), ("m_slopeAngle", 7, null),
                      ("m_topAngle", 7, null), ("m_famSymId", 14, 20), ("m_instId", 14, 20),
                      ("m_symId", 14, 20), ("m_deleted", 1, 493) ];
                    if (count > 100_000 || members?.Count != expected.Length
                        || members.Where((m, i) => m.Declaring != 493 || m.Mods != 0 || m.Cnt != null
                            || m.Name != expected[i].Name || m.Type != expected[i].Type || m.Ref != expected[i].Ref).Any()) return null;
                    // The array stores class 493 records inline, without polymorphic pointer tags.
                    for (int row = 0; row < count; row++)
                        foreach (var member in members)
                        {
                            int memberStart = cursor.Pos;
                            ReadField(cursor, member, queue, 1, null);
                            if (member.Name == "m_deleted" && data[memberStart] > 1) return null;
                        }
                    continue;
                }
                ReadField(cursor, field, queue, 0, null);
                if (field is { Declaring: 37, Name: "m_id" }) owner = BitConverter.ToInt32(data, position);
                if (field is { Declaring: 485, Name: "m_baseRailingSymbolId" }) symbol = BitConverter.ToInt32(data, position);
                if (field is { Declaring: 492, Name: "m_baseRailingId" }) railing = BitConverter.ToInt32(data, position);
                if (field is { Declaring: 492, Name: "m_approxLength" }) length = BitConverter.ToDouble(data, position);
            }
        }
        catch (DecodeFail) { return null; }
        return owner is > 0 && owner == BitConverter.ToInt32(data, start)
            ? new(classId, owner.Value, symbol, railing, length) : null;
    }
}
