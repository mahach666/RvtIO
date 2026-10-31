namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record StairNameParameters(int OwnerId, NativeParams Values);

    internal static string? ExtractOwnStringParameterDefinitionName(byte[] data, int start, uint size, ushort classId)
    {
        if (classId != 2873 || !TryExtractOwnElementClass(data, start, size, classId, out int owner)) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = checked((int)((long)start + 12 + size)), RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        int definition = -1; string? name = null; int definitionOwner = 0;
        try
        {
            foreach (var field in SerializationFields(classId) ?? throw Fail)
            {
                int before = queue.Count;
                ReadField(cursor, field, queue, 0, null);
                if (field is { Declaring: 2870, Name: "m_pParamDef", Type: 14, Mods: 1, Ref: null })
                {
                    if (queue.Count != before + 1 || queue[before].Cls != 606) throw Fail;
                    definition = before;
                }
                if (queue.Count > 100_000) throw Fail;
            }
            if (definition < 0) throw Fail;
            for (int index = 0; index < queue.Count; index++)
            {
                if (queue.Count > 100_000) throw Fail;
                if (index != definition) { ReadDeferredObject(cursor, queue[index].Cls, queue); continue; }
                foreach (var field in SerializationFields(606) ?? throw Fail)
                {
                    if (field is { Declaring: 301, Name: "m_caption", Type: 8, Mods: 96 }) name = ReadString(cursor);
                    else if (field is { Declaring: 301, Name: "m_paramElemId", Type: 14, Mods: 0, Ref: 20 }) definitionOwner = ReadI32(cursor);
                    else ReadField(cursor, field, queue, 1, null);
                }
            }
            // Caption display/accelerator forms are outside this literal-name contract.
            if (cursor.Pos != cursor.End || definitionOwner != owner || string.IsNullOrEmpty(name)
                || name.Contains('\0') || name[0] is '&' or '\u2026') throw Fail;
            return name;
        }
        catch (DecodeFail) { return null; }
        catch (OverflowException) { return null; }
    }

    /// <summary>Own parameter stores of a native stair/component. Supports currently admit four validated null stores only.
    /// Later root/geometry data is outside that absence validation and never a source of parameter values.</summary>
    internal static StairNameParameters? ExtractStairNameParameters(byte[] data, int start, uint size, ushort classId)
    {
        if (classId is not (3681 or 3708 or 3686 or 3727)
            || !TryExtractOwnElementClass(data, start, size, classId, out int owner)) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = checked((int)((long)start + 12 + size)), RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        var stores = new List<(int Index, char Kind, ushort RecordClass)>();
        string[] names = ["m_pParamValueSetDouble", "m_pParamValueSetInt", "m_pParamValueSetAString", "m_pParamValueSetElementId"];
        ushort[] tags = [2887, 2889, 2886, 2888];
        var values = new NativeParams();
        try
        {
            // Support.m_hostRef has an unresolved inline descriptor. Its later root/FIFO
            // cannot locate a non-null store safely. The own Element base plus four
            // explicit null pointers confirms absence only.
            var root = SerializationFields(classId == 3727 ? 37 : classId) ?? throw Fail;
            if (root.Count < 4) throw Fail;
            for (int index = 0; index < root.Count; index++)
            {
                var field = root[index]; int before = queue.Count; int fieldStart = cursor.Pos;
                if (index < 4 && (field.Declaring != 37 || field.Name != names[index]
                    || field.Type != 14 || field.Mods != 1 || field.Ref != null)) throw Fail;
                if (classId == 3727 && index < 4
                    && (cursor.Pos > cursor.End - 4 || BitConverter.ToUInt32(data, cursor.Pos) != 0)) throw Fail;
                if (field is { Declaring: 3687, Name: "m_compatibleCrvInfo", Type: 14, Mods: 0, Ref: null })
                {
                    if (!StairGraphLayout.TryLayoutInfo(data, cursor.Pos, cursor.End, out int finish, out _, out var additions)) throw Fail;
                    cursor.Pos = finish; queue.AddRange(additions);
                }
                else if (field is { Declaring: 3687, Name: "m_logicJoinInfo", Type: 14, Mods: 0, Ref: null }
                    or { Declaring: 3686, Name: "m_landingRisersMap", Type: 14, Mods: 80, Ref: 867 })
                {
                    uint count = ReadU32(cursor); if (count == uint.MaxValue) count = 0;
                    int width = field.Name == "m_logicJoinInfo" ? 16 : 12;
                    if (count > 100_000 || count * (long)width > cursor.End - cursor.Pos) throw Fail;
                    cursor.Pos += checked((int)count * width);
                }
                else ReadField(cursor, field, queue, 0, null);
                if (queue.Count > 100_000) throw Fail;
                if (index < 4 && queue.Count != before)
                {
                    if (queue.Count != before + 1 || queue[before].Cls != tags[index]) throw Fail;
                    var tag = ParamSetTags[tags[index]];
                    stores.Add((before, tag.Kind, checked((ushort)tag.RecordClass)));
                }
                // A non-null external pointer cannot establish an absent store.
                if (index < 4 && queue.Count == before
                    && (cursor.Pos != fieldStart + 4 || BitConverter.ToUInt32(data, fieldStart) != 0)) throw Fail;
            }
            for (int storeIndex = 0; storeIndex < stores.Count; storeIndex++)
            {
                var (index, kind, recordClass) = stores[storeIndex];
                if (index != storeIndex) throw Fail;
                uint count = ReadU32(cursor); if (count > 100_000) throw Fail;
                for (uint row = 0; row < count; row++)
                {
                    int pid = 0, iv = 0; double dv = 0; string sv = "";
                    foreach (var field in SerializationFields(recordClass) ?? throw Fail)
                    {
                        if (field.Name == "m_paramId") pid = ReadI32(cursor);
                        else if (field.Name == "m_value")
                        {
                            if (kind == 'D') dv = ReadF64(cursor);
                            else if (kind == 'A') sv = ReadString(cursor);
                            else iv = ReadI32(cursor);
                        }
                        else throw Fail;
                    }
                    switch (kind)
                    {
                        case 'D': values.Doubles.Add((pid, dv)); break;
                        case 'I': values.Ints.Add((pid, iv)); break;
                        // The native conversion is NUL-terminated; a persisted embedded NUL
                        // cannot be treated as the same literal value.
                        case 'A': if (sv.Contains('\0')) throw Fail; values.Strings.Add((pid, sv)); break;
                        case 'E': values.ElementIds.Add((pid, iv)); break;
                    }
                }
            }
            values.RootParameterStoresComplete = true;
            return new(owner, values);
        }
        catch (DecodeFail) { return null; }
        catch (OverflowException) { return null; }
    }
}
