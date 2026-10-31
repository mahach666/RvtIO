using System.Reflection;
using System.Text;
using RvtIO.Parsing;

// Diagnostic only. Reads the complete own UnitsElem graph, keeping native units, all
// FormatOptions fields and family/document ownership separate. No unit is chosen from names or ids.
internal static class UnitOptionsProbe
{
    internal sealed record FormatOption(string Spec, string Symbol, string Unit, double Accuracy,
        int RoundingMethod, byte SuppressLeadingZeros, byte SuppressSpaces,
        byte SuppressTrailingZeros, byte UseDefault, byte UseGrouping, byte UsePlusPrefix);
    internal sealed record Units(FormatOption[] Options, int UniversalUnitSystem,
        int DecimalSymbol, int DigitGroupingSymbol, int DigitGroupingAmount);
    internal sealed record OwnUnits(uint Owner, int FamilyId, int DocumentAccess,
        int UnitsStart, int UnitsEnd, Units Value);

    private static readonly UnicodeEncoding Utf16 = new(false, false, true);

    internal static bool TryReadUnits(byte[] bytes, int start, int end, out int finish, out Units? units)
    {
        finish = -1; units = null;
        if (start < 0 || end < 0 || end > bytes.Length || start > end - 4) return false;
        int pos = start;
        uint count = BitConverter.ToUInt32(bytes, pos); pos += 4;
        if (count == uint.MaxValue) count = 0;
        // Each entry has three string lengths and a fixed 18-byte options tail.
        if (count > 100_000 || count * 30L + 16 > end - pos) return false;
        var options = new FormatOption[(int)count];
        var specs = new HashSet<string>(StringComparer.Ordinal);
        bool String(out string value)
        {
            value = "";
            if (pos > end - 4) return false;
            uint length = BitConverter.ToUInt32(bytes, pos); pos += 4;
            if (length > 10_000 || length * 2L > end - pos) return false;
            try { value = Utf16.GetString(bytes, pos, (int)length * 2); }
            catch (DecoderFallbackException) { return false; }
            pos += (int)length * 2;
            return true;
        }
        for (int index = 0; index < options.Length; index++)
        {
            if (!String(out string spec) || !String(out string symbol) || !String(out string unit)
                || !specs.Add(spec) || pos > end - 18) return false;
            double accuracy = BitConverter.ToDouble(bytes, pos);
            int rounding = BitConverter.ToInt32(bytes, pos + 8);
            if (!double.IsFinite(accuracy) || accuracy < 0) return false;
            for (int flag = 12; flag < 18; flag++)
                if (bytes[pos + flag] > 1) return false;
            options[index] = new(spec, symbol, unit, accuracy, rounding,
                bytes[pos + 12], bytes[pos + 13], bytes[pos + 14],
                bytes[pos + 15], bytes[pos + 16], bytes[pos + 17]);
            pos += 18;
        }
        if (pos > end - 16) return false;
        units = new(options, BitConverter.ToInt32(bytes, pos), BitConverter.ToInt32(bytes, pos + 4),
            BitConverter.ToInt32(bytes, pos + 8), BitConverter.ToInt32(bytes, pos + 12));
        finish = pos + 16;
        return true;
    }

    internal static OwnUnits? ReadOwnBlock(byte[] bytes, uint size, ushort classId)
    {
        if (classId != 4029 || bytes.Length < 20 || size != bytes.Length - 16
            || BitConverter.ToUInt32(bytes, 8) != size || BitConverter.ToUInt16(bytes, 12) != classId
            || BitConverter.ToUInt32(bytes, bytes.Length - 4) != size) return null;
        uint owner = BitConverter.ToUInt32(bytes, 0);
        if (owner == 0 || owner > int.MaxValue) return null;
        var rootFields = TfBlockDeserializer.SerializationFields(classId);
        if (rootFields == null || !SchemasMatch()) return null;
        Type type = typeof(TfBlockDeserializer);
        Type cursorType = type.GetNestedType("Cursor", BindingFlags.NonPublic)!;
        var read = type.GetMethod("ReadField", BindingFlags.NonPublic | BindingFlags.Static)!;
        var deferred = type.GetMethod("ReadDeferredObject", BindingFlags.NonPublic | BindingFlags.Static)!;
        object cursor = Activator.CreateInstance(cursorType)!;
        cursorType.GetField("D")!.SetValue(cursor, bytes);
        cursorType.GetField("Pos")!.SetValue(cursor, 14);
        cursorType.GetField("End")!.SetValue(cursor, bytes.Length - 4);
        cursorType.GetField("RootClassId")!.SetValue(cursor, classId);
        var root = new TfBlockDeserializer.NativeParams();
        var queue = new List<(ushort, uint)>();
        Units? units = null;
        int unitsStart = -1, unitsEnd = -1;
        try
        {
            foreach (var field in rootFields)
            {
                if (field is { Declaring: 4029, Name: "m_units", Type: 14, Mods: 0, Ref: 82 })
                {
                    if (units != null) return null;
                    unitsStart = (int)cursorType.GetField("Pos")!.GetValue(cursor)!;
                    if (!TryReadUnits(bytes, unitsStart, bytes.Length - 4, out unitsEnd, out units)) return null;
                    cursorType.GetField("Pos")!.SetValue(cursor, unitsEnd);
                }
                else read.Invoke(null, [cursor, field, queue, 0, root]);
            }
            for (int index = 0; index < queue.Count; index++)
            {
                if (index > 100_000) return null;
                deferred.Invoke(null, [cursor, queue[index].Item1, queue]);
            }
        }
        catch (TargetInvocationException exception) when (exception.InnerException?.GetType().Name == "DecodeFail")
        { return null; }
        if (units == null || (int)cursorType.GetField("Pos")!.GetValue(cursor)! != bytes.Length - 4
            || !root.Fields.TryGetValue("m_id", out int rootOwner) || rootOwner != owner
            || !root.Fields.TryGetValue("m_famId", out int family)
            || !root.Fields.TryGetValue("m_docAccess", out int document)) return null;
        return new(owner, family, document, unitsStart, unitsEnd, units);
    }

    private static bool SchemasMatch()
    {
        var schemas = TfBlockDeserializer.Schemas;
        if (!schemas.TryGetValue(82, out var units) || units.Name != "AUnits" || units.Fields.Count != 5
            || units.Fields[0] is not { Name: "m_formatOptionsMap", Type: 14, Mods: 80, Ref: null }
            || !schemas.TryGetValue(83, out var pair) || pair.Name != "std::pair< ForgeTypeId, FormatOptions >"
            || pair.Fields.Count != 2 || pair.Fields[0] is not { Name: "first", Type: 14, Mods: 0, Ref: 45 }
            || pair.Fields[1] is not { Name: "second", Type: 14, Mods: 0, Ref: null }
            || !schemas.TryGetValue(45, out var forge) || forge.Name != "ForgeTypeId" || forge.Fields.Count != 1
            || forge.Fields[0] is not { Name: "m_typeId", Type: 8, Mods: 96 }
            || !schemas.TryGetValue(84, out var format) || format.Name != "FormatOptions" || format.Fields.Count != 10)
            return false;
        string[] tail = ["m_universalUnitSystem", "m_decimalSymbol", "m_digitGroupingSymbol", "m_digitGroupingAmount"];
        for (int index = 0; index < tail.Length; index++)
            if (units.Fields[index + 1].Name != tail[index] || units.Fields[index + 1].Type != 4
                || units.Fields[index + 1].Mods != 0) return false;
        string[] names = ["m_symbolTypeId", "m_unitTypeId", "m_accuracy", "m_roundingMethod",
            "m_bSuppressLeadingZeros", "m_bSuppressSpaces", "m_bSuppressTrailingZeros", "m_bUseDefault",
            "m_bUseGrouping", "m_bUsePlusPrefix"];
        for (int index = 0; index < names.Length; index++)
        {
            var field = format.Fields[index];
            byte expectedType = index < 2 ? (byte)14 : index == 2 ? (byte)7 : index == 3 ? (byte)4 : (byte)1;
            if (field.Name != names[index] || field.Type != expectedType || field.Mods != 0
                || index < 2 && field.Ref != 45) return false;
        }
        return true;
    }
}
