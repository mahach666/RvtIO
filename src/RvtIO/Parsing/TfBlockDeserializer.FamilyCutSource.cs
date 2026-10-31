namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record OwnFamilyCutSymbol(int FamilyId, bool Active);

    internal static OwnFamilyCutSymbol? ExtractOwnFamilyCutSymbol(
        byte[] data, int start, uint size, ushort classId)
    {
        if (classId != 1829 || !TryExtractOwnElementClass(data, start, size, classId, out _)) return null;
        var fields = SerializationFields(classId);
        if (fields == null || !HasExactSchemaField(fields, 1829, "m_familyId", 14, 0, 20)
            || !HasExactSchemaField(fields, 1829, "m_active", 1, 0, null)) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = start + 12 + (int)size,
            RootClassId = classId, ExactFamilyCutInputs = true };
        var queue = new List<(ushort Cls, uint Id)>();
        int familyId = 0;
        try
        {
            foreach (var field in fields)
            {
                if (field.Declaring == 1829 && field.Name == "m_familyId") familyId = ReadI32(cursor);
                else if (field.Declaring == 1829 && field.Name == "m_active")
                {
                    if (!cursor.Take(1) || familyId <= 0) return null;
                    return new(familyId, data[cursor.Pos - 1] != 0);
                }
                else ReadField(cursor, field, queue, 0, null);
                if (queue.Count > 100_000) return null;
            }
        }
        catch (DecodeFail) { return null; }
        return null;
    }

    internal static bool? ExtractOwnFamilyInPlace(byte[] data, int start, uint size, ushort classId)
    {
        if (classId != 1772 || !TryExtractOwnElementClass(data, start, size, classId, out _)) return null;
        var fields = SerializationFields(classId);
        if (fields == null || !HasExactSchemaField(fields, 1773, "m_inPlace", 1, 0, null)) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = start + 12 + (int)size, RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        try
        {
            foreach (var field in fields)
            {
                if (field.Declaring == 1773 && field.Name == "m_inPlace")
                    return cursor.Take(1) ? data[cursor.Pos - 1] != 0 : null;
                ReadField(cursor, field, queue, 0, null);
                if (queue.Count > 100_000) return null;
            }
        }
        catch (DecodeFail) { return null; }
        return null;
    }
}
