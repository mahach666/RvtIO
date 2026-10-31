namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallPhasePointer(uint Handle, ushort? ClassId, int? QueueIndex, int Start, int End);
    internal sealed record WallPhaseArray(string Name, uint Count, int Start, int End,
        IReadOnlyList<WallPhasePointer> Members);
    internal sealed record WallPhaseDeferredObject(int QueueIndex, ushort ClassId, uint Handle, int Start, int End);
    internal sealed record OwnWallGeometryPhases(int OwnerId, ushort ClassId, int RootEnd,
        WallPhasePointer ListPointer, int Start, int End, IReadOnlyList<WallPhaseDeferredObject> PriorObjects,
        IReadOnlyList<WallPhaseArray> Phases, IReadOnlyList<WallPhasePointer> Snapshots,
        uint OwnerReference, IReadOnlyList<int> LatestStepTypes, int IdCounter, byte Flags);

    /// <summary>Complete own wall root and its named Element.m_geomSteps FIFO object. All fourteen GeomStepList fields
    /// are consumed; array slots (including null/external pointers) and their order are kept. Later step/snapshot
    /// payloads, reference-handle resolution and CanExportWallGeometryAsExtrusion are not covered.</summary>
    internal static OwnWallGeometryPhases? ExtractOwnWallGeometryPhases(
        byte[] data, int start, uint size, ushort classId, out string? failure)
        => ExtractOwnWallGeometryPhasesCore(data, start, size, classId, out failure, out _, out _, out _);

    private static OwnWallGeometryPhases? ExtractOwnWallGeometryPhasesCore(
        byte[] data, int start, uint size, ushort classId, out string? failure,
        out Cursor? continuation, out List<(ushort Cls, uint Id)>? deferred,
        out WallPhasePointer? geometryTablePointer)
    {
        continuation = null;
        deferred = null;
        geometryTablePointer = null;
        failure = "frame";
        if (classId is not (3524 or 361) || start < 0 || start > data.Length - 16 || size < 4
            || size > int.MaxValue || (long)start + 16 + size > data.Length
            || BitConverter.ToUInt32(data, start + 8) != size
            || BitConverter.ToUInt16(data, start + 12) != classId
            || BitConverter.ToUInt32(data, start + 12 + (int)size) != size)
            return null;
        string[] names = ["m_nonBRepGList", "m_bRepFormGList", "m_bRepAdjustGList",
            "m_bRepCutOutGList", "m_bRepPostCutOutGList", "m_bRepTweakGList"];
        string[] snapshots = ["m_bRepFormSnapshot", "m_bRepAdjustSnapshot",
            "m_bRepCutOutSnapshot", "m_bRepPostCutOutSnapshot"];
        var rootFields = SerializationFields(classId);
        var fields = SerializationFields(2086);
        failure = "schema";
        if (rootFields == null || fields is not { Count: 14 }
            || !HasExactSchemaField(rootFields, 37, "m_id", 14, 0, 20)
            || !HasExactSchemaField(rootFields, 37, "m_docAccess", 14, 0, null)
            || !HasExactSchemaField(rootFields, 37, "m_geomSteps", 14, 1, null)
            || !HasExactSchemaField(rootFields, 37, "m_pGeomTable", 14, 1, null)
            || names.Where((name, index) => fields[index] is not { Declaring: 2086, Type: 14,
                Mods: 81, Ref: null, Cnt: null } || fields[index].Name != name).Any()
            || snapshots.Where((name, index) => fields[index + 6] is not { Declaring: 2086, Type: 14,
                Mods: 1, Ref: null, Cnt: null } || fields[index + 6].Name != name).Any()
            || fields[10] is not { Declaring: 2086, Name: "m_pElem", Type: 14, Mods: 3, Ref: null, Cnt: null }
            || fields[11] is not { Declaring: 2086, Name: "m_latestGStepTypeInPrevRegenCycle", Type: 4,
                Mods: 16, Cnt: 5, Ref: null }
            || fields[12] is not { Declaring: 2086, Name: "m_idCounter", Type: 4, Mods: 0, Ref: null, Cnt: null }
            || fields[13] is not { Declaring: 2086, Name: "m_flags", Type: 2, Mods: 0, Ref: null, Cnt: null })
            return null;
        var cursor = new Cursor { D = data, Pos = start + 14,
            End = start + 12 + (int)size, RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        int owner = 0;
        WallPhasePointer? list = null;
        WallPhasePointer? geometry = null;
        try
        {
            foreach (var field in rootFields)
            {
                failure = $"root:{field.Declaring}.{field.Name}";
                if (field.Declaring == 37 && field.Name == "m_id") owner = ReadI32(cursor);
                else if (field.Declaring == 37 && field.Name == "m_docAccess")
                { if (ReadU32(cursor) != 1) return null; }
                else if (field.Declaring == 37 && field.Name == "m_geomSteps") list = Pointer();
                else if (field.Declaring == 37 && field.Name == "m_pGeomTable") geometry = Pointer();
                else ReadField(cursor, field, queue, 0, null);
                if (queue.Count > 100_000) return null;
            }
            failure = "owner";
            if (owner <= 0 || owner != BitConverter.ToInt32(data, start)) return null;
            failure = "list-pointer";
            if (list is not { Handle: uint.MaxValue, ClassId: 2086, QueueIndex: int target }) return null;
            int rootEnd = cursor.Pos - start;
            var prior = new List<WallPhaseDeferredObject>();
            for (int index = 0; index < target; index++)
            {
                failure = $"fifo:{index}:{queue[index].Cls}";
                int objectStart = cursor.Pos - start;
                ReadDeferredObject(cursor, queue[index].Cls, queue);
                if (queue.Count > 100_000) return null;
                prior.Add(new(index, queue[index].Cls, queue[index].Id, objectStart, cursor.Pos - start));
            }
            int listStart = cursor.Pos - start;
            var phases = new List<WallPhaseArray>();
            foreach (string name in names)
            {
                failure = $"list:{name}";
                int arrayStart = cursor.Pos - start;
                uint count = ReadU32(cursor);
                var members = new List<WallPhasePointer>();
                if (count != uint.MaxValue)
                {
                    if (count > 1_000_000 || count > (cursor.End - cursor.Pos) / 4) return null;
                    for (uint index = 0; index < count; index++) members.Add(Pointer());
                }
                phases.Add(new(name, count, arrayStart, cursor.Pos - start, members));
            }
            var snapshotValues = new List<WallPhasePointer>();
            foreach (string name in snapshots)
            { failure = $"list:{name}"; snapshotValues.Add(Pointer()); }
            failure = "list:m_pElem";
            uint reference = ReadU32(cursor); // non-owning raw handle, NOT an ElementId or class-tagged pointer
            failure = "list:m_latestGStepTypeInPrevRegenCycle";
            int[] latest = [ReadI32(cursor), ReadI32(cursor), ReadI32(cursor), ReadI32(cursor), ReadI32(cursor)];
            failure = "list:m_idCounter";
            int counter = ReadI32(cursor);
            failure = "list:m_flags";
            if (!cursor.Take(1)) return null;
            byte flags = data[cursor.Pos - 1];
            failure = null;
            continuation = cursor;
            deferred = queue;
            geometryTablePointer = geometry;
            return new(owner, classId, rootEnd, list, listStart, cursor.Pos - start, prior,
                phases, snapshotValues, reference, latest, counter, flags);
        }
        catch (DecodeFail) { return null; }

        WallPhasePointer Pointer()
        {
            int pointerStart = cursor.Pos, before = queue.Count;
            ReadPtr(cursor, queue);
            if (queue.Count > 100_000) throw Fail;
            uint handle = BitConverter.ToUInt32(data, pointerStart);
            ushort? pointerClass = handle == 0 ? null : BitConverter.ToUInt16(data, pointerStart + 4);
            return new(handle, pointerClass, queue.Count == before ? null : before,
                pointerStart - start, cursor.Pos - start);
        }
    }
}
