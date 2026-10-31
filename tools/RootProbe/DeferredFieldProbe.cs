using System.Reflection;
using RvtIO.Parsing;

// Diagnostic field trace of one already located deferred object. It does not resolve
// handles or change shared schemas, and stops at the first failed field without resync.
internal static class DeferredFieldProbe
{
    internal sealed record Field(int Declaring, string Name, byte Type, byte Mods, int? Ref,
        int Start, int End, string Bytes, string? Failure, string? NativeLayout = null);
    internal sealed record Result(int End, string? Failure, Field[] Fields,
        (ushort ClassId, uint Handle)[] Queued);

    // Frame/own Element identity only. Subtype fields and FIFO must still be read.
    internal static bool HasOwnFamilyFrame(byte[] bytes, uint size, ushort classId, int expectedOwner)
        => classId is 1794 or 1829 or 1772 && expectedOwner > 0 && size == (long)bytes.Length - 16
            && TfBlockDeserializer.TryExtractOwnElementClass(bytes, 0, size, classId, out int owner)
            && owner == expectedOwner;

    internal static Result Read(byte[] bytes, int start, int end, ushort classId, ushort rootClassId)
    {
        if (start < 0 || end < start || end > bytes.Length)
            return new(start, "Invalid deferred bounds", [], []);
        var fields = TfBlockDeserializer.SerializationFields(classId);
        if (fields == null) return new(start, "Missing deferred schema", [], []);
        Type type = typeof(TfBlockDeserializer);
        Type cursorType = type.GetNestedType("Cursor", BindingFlags.NonPublic)!;
        MethodInfo read = type.GetMethod("ReadField", BindingFlags.NonPublic | BindingFlags.Static)!;
        object cursor = Activator.CreateInstance(cursorType)!;
        cursorType.GetField("D")!.SetValue(cursor, bytes);
        cursorType.GetField("Pos")!.SetValue(cursor, start);
        cursorType.GetField("End")!.SetValue(cursor, end);
        cursorType.GetField("RootClassId")!.SetValue(cursor, rootClassId);
        var queue = new List<(ushort, uint)>();
        var positions = new List<Field>();
        string? failure = null;
        foreach (var field in fields)
        {
            int fieldStart = (int)cursorType.GetField("Pos")!.GetValue(cursor)!;
            string? nativeLayout = null;
            try
            {
                if (rootClassId == 1794 && classId == 1285
                    && field is { Declaring: 1285, Name: "m_LargestMotorMap", Type: 14, Mods: 0, Ref: null })
                {
                    // The schema dictionary omits this field ref and makes 1289.second self-recursive;
                    // corrected here only, never in the shared schema.
                    var pair = TfBlockDeserializer.SerializationFields(1289);
                    var scalarPair = TfBlockDeserializer.SerializationFields(22);
                    if (pair is not { Count: 2 } || scalarPair is not { Count: 2 }
                        || pair[0] is not { Declaring: 1289, Name: "first", Type: 14, Mods: 0, Ref: 20 }
                        || pair[1] is not { Declaring: 1289, Name: "second", Type: 14, Mods: 0, Ref: 1289 }
                        || scalarPair[0] is not { Declaring: 22, Name: "first", Type: 14, Mods: 0, Ref: 20 }
                        || scalarPair[1] is not { Declaring: 22, Name: "second", Type: 7, Mods: 0, Ref: 22 })
                        return new(fieldStart, "Unsupported native largest-motor pair schema", [.. positions], [.. queue]);
                    nativeLayout = "1289:first ElementId, second inline 22(ElementId,double)";
                    read.Invoke(null, [cursor, pair[0], queue, 1, null]);
                    read.Invoke(null, [cursor, pair[1] with { Ref = 22 }, queue, 1, null]);
                }
                else if (rootClassId == 1794 && classId == 1285
                    && field is { Declaring: 1285, Type: 14, Mods: 80, Ref: 24 }
                    && field.Name is "m_LCApparentLoadParamMap" or "m_LCDemandLoadParamMap"
                        or "m_LCDemandFactorParamMap" or "m_LCApparentCurrentParamMap" or "m_LCDemandCurrentParamMap")
                {
                    // Count plus map of (ElementId, ElementId) pairs, two four-byte leaves. The alias
                    // is scoped to this owner, class and field; the shared descriptor is unchanged.
                    nativeLayout = "24:first ElementId, second ElementId";
                    ReadLeafPairs(field, 24, 20);
                }
                else if (rootClassId == 1772 && classId == 1740
                    && field is { Declaring: 1740, Name: "m_big2SmallMap2", Type: 14, Mods: 80, Ref: 866 })
                {
                    // Pairs of ElementId and ForeignElemRef, two 4-byte leaves.
                    nativeLayout = "866:first ElementId, second inline 141(ForeignElemRef)";
                    ReadLeafPairs(field, 866, 141);
                }
                else if (rootClassId == 1772 && classId == 1740
                    && field is { Declaring: 1740, Name: "m_coreIds", Type: 14, Mods: 80, Ref: 1740 })
                {
                    // A set of ElementId; the declared reference resolves to ElementId, not to FamilyDocument.
                    nativeLayout = "Set<ElementId>";
                    read.Invoke(null, [cursor, field with { Ref = 20 }, queue, 1, null]);
                }
                else if (rootClassId == 1772 && classId == 775
                    && field is { Declaring: 775, Name: "m_propId2FamParamIdBindings", Type: 14, Mods: 80, Ref: 24 })
                {
                    // Count plus map of (ElementId, ElementId) pairs, two 4-byte leaves.
                    nativeLayout = "24:first ElementId, second ElementId";
                    ReadLeafPairs(field, 24, 20);
                }
                else if (rootClassId == 1772 && classId == 90
                    && field is { Declaring: 90, Name: "m_data", Type: 14, Mods: 80, Ref: null })
                {
                    // The items are the exact ElementIdPlusIndex class (91), not polymorphic handles.
                    var item = TfBlockDeserializer.SerializationFields(91);
                    if (item is not { Count: 2 }
                        || item[0] is not { Declaring: 91, Name: "m_elementId", Type: 14, Mods: 0, Ref: 20 }
                        || item[1] is not { Declaring: 91, Name: "m_index", Type: 4, Mods: 0, Ref: 90 })
                        throw new InvalidDataException("Unsupported native ElementIdPlusIndex schema");
                    nativeLayout = "Set<91(ElementIdPlusIndex)>";
                    read.Invoke(null, [cursor, field with { Ref = 91 }, queue, 1, null]);
                }
                else if (rootClassId == 1772 && classId == 1812
                    && field is { Declaring: 1812, Name: "m_idxToRefMap", Type: 14, Mods: 80, Ref: null })
                {
                    // Pairs of int and an inline (int, AString) pair.
                    var pair = TfBlockDeserializer.SerializationFields(1813);
                    var value = TfBlockDeserializer.SerializationFields(1814);
                    if (pair is not { Count: 2 } || value is not { Count: 2 }
                        || pair[0] is not { Declaring: 1813, Name: "first", Type: 4, Mods: 0, Ref: null }
                        || pair[1] is not { Declaring: 1813, Name: "second", Type: 14, Mods: 0, Ref: null }
                        || value[0] is not { Declaring: 1814, Name: "first", Type: 4, Mods: 0, Ref: null }
                        || value[1] is not { Declaring: 1814, Name: "second", Type: 8, Mods: 96, Ref: 1813 })
                        throw new InvalidDataException("Unsupported native reference-index pair schema");
                    nativeLayout = "Map<int,1814(int,AString)>";
                    ReadPairs(field, pair[0], pair[1] with { Ref = 1814 });
                }
                else if (rootClassId == 1772 && classId == 1809
                    && field is { Declaring: 1809, Name: "m_sortedParams", Type: 14, Mods: 80, Ref: null })
                {
                    // Items are GroupAndParamsPair(1810): a parameter-id vector followed by an ElementId group.
                    var item = TfBlockDeserializer.SerializationFields(1810);
                    if (item is not { Count: 2 }
                        || item[0] is not { Declaring: 1810, Name: "m_paramIds", Type: 14, Mods: 80, Ref: 20 }
                        || item[1] is not { Declaring: 1810, Name: "m_groupId", Type: 14, Mods: 0, Ref: 1810 })
                        throw new InvalidDataException("Unsupported native GroupAndParamsPair schema");
                    nativeLayout = "Arr<1810(Arr<ElementId>,ElementId)>";
                    ReadPairs(field, item[0], item[1] with { Ref = 20 });
                }
                else read.Invoke(null, [cursor, field, queue, 1, null]);
                if (queue.Count > 100_000) failure = "Unbounded deferred queue";
            }
            catch (TargetInvocationException exception) when (exception.InnerException?.GetType().Name == "DecodeFail")
            {
                failure = exception.InnerException.GetType().Name + ":" + exception.InnerException.Message;
            }
            catch (InvalidDataException exception)
            {
                failure = exception.Message;
            }
            int fieldEnd = (int)cursorType.GetField("Pos")!.GetValue(cursor)!;
            positions.Add(new(field.Declaring, field.Name, field.Type, field.Mods, field.Ref,
                fieldStart, fieldEnd, Convert.ToHexString(bytes.AsSpan(fieldStart, fieldEnd - fieldStart)), failure, nativeLayout));
            if (failure != null) break;
        }
        return new((int)cursorType.GetField("Pos")!.GetValue(cursor)!, failure, [.. positions], [.. queue]);

        void ReadLeafPairs(TfBlockDeserializer.TfField field, int pairClass, int secondClass)
        {
            var pair = TfBlockDeserializer.SerializationFields(pairClass);
            var foreign = TfBlockDeserializer.SerializationFields(141);
            if (pair is not { Count: 2 }
                || pair[0] is not { Name: "first", Type: 14, Mods: 0, Ref: 20 }
                || pair[0].Declaring != pairClass
                || pair[1] is not { Name: "second", Type: 14, Mods: 0 }
                || pair[1].Ref != pairClass || pair[1].Declaring != pairClass
                || secondClass == 141 && (foreign is not { Count: 1 }
                    || foreign[0] is not { Declaring: 141, Name: "m_id", Type: 4, Mods: 0, Ref: 141 }))
                throw new InvalidDataException("Unsupported native leaf-pair schema");
            ReadPairs(field, pair[0], pair[1] with { Ref = secondClass });
        }

        void ReadPairs(TfBlockDeserializer.TfField field,
            TfBlockDeserializer.TfField first, TfBlockDeserializer.TfField second)
        {
            int countStart = (int)cursorType.GetField("Pos")!.GetValue(cursor)!;
            read.Invoke(null, [cursor, field with { Type = 5, Mods = 0, Ref = null }, queue, 1, null]);
            uint count = BitConverter.ToUInt32(bytes, countStart);
            if (count == uint.MaxValue) return;
            if (count > 100_000) throw new InvalidDataException("Unbounded native pair count");
            for (uint index = 0; index < count; index++)
            {
                read.Invoke(null, [cursor, first, queue, 1, null]);
                read.Invoke(null, [cursor, second, queue, 1, null]);
            }
        }
    }
}
