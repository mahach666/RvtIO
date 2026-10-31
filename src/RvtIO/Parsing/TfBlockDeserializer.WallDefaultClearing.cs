namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallDebugOffSideId(int Side, IReadOnlyList<int> HistoryKeys, int Id, int? HistoryRowIndex);
    internal sealed record WallDebugOffClearCallback(int PhaseIndex, int StepQueueIndex, ushort ClassId,
        int Flags, int? GeneratorQueueIndex, ushort? GeneratorClassId, IReadOnlyList<WallDebugOffSideId> Sides);
    internal sealed record OwnWallDebugOffClearIds(int OwnerId, ushort ClassId, int GeometryCount,
        int? WallCrossSection, int? ExtrusionHelperQueueIndex, IReadOnlyList<WallDebugOffClearCallback> Callbacks);
    internal sealed record WallClearIdVisit(int StepQueueIndex, int Side, int Id, WallTableNode? Previous, string Outcome);
    internal sealed record OwnWallDebugOffClearedTable(int OwnerId, ushort ClassId, int MaxId,
        int GeometryCount, IReadOnlyList<WallClearIdVisit> Visits, IReadOnlyList<WallPhysicalTableSlot> Slots);

    /// <summary>Callback 37 ids with WallUsePredefinedFaceHistories off. The debug mode is runtime state, not a field
    /// read from the file; default construction initializes it off, but configured debug states are outside this API.
    /// A helper selects the own form-phase ExtrusionGStep, not Sketch. No callback 36 skip flag applies here.
    /// Zero local bodies skip all callbacks.</summary>
    internal static OwnWallDebugOffClearIds? SelectOwnWallDebugOffClearIds(OwnWallCompleteInputs input,
        OwnWallReferenceGeometryInputs references, int geometryCount, out string? failure)
    {
        var history = input.Histories.Histories; var list = history.List;
        failure = "clear-owner";
        if (geometryCount < 0 || list.ClassId is not (3524 or 361) || list.OwnerId <= 0
            || list.OwnerId != references.OwnerId || list.OwnerReference != 2
            || references.ListOwnerReference != 2 || !history.Complete) return null;
        if (geometryCount == 0)
        { failure = null; return new(list.OwnerId, list.ClassId, 0, null, null, []); }
        var steps = history.Steps.ToDictionary(s => s.QueueIndex);
        var callbacks = new List<WallDebugOffClearCallback>();
        int? cross = null, helper = null;
        WallGeometryStepPrefix? generator = null;
        bool contextRead = false;
        string context = "clear";
        try
        {
            for (int phase = 0; phase < list.Phases.Count; phase++)
                foreach (var pointer in list.Phases[phase].Members)
                {
                    context = $"clear-callback:{phase}:{pointer.QueueIndex}";
                    if (pointer.QueueIndex is not int index || !steps.TryGetValue(index, out var step)
                        || step.ClassId != pointer.ClassId || step.OwnerReference != 2) throw Fail;
                    var sides = new List<WallDebugOffSideId>();
                    if (step.ClassId == 596)
                    {
                        // The binding is SWall's (class 239). The CurvedWall override
                        // is distinct and not generalized from this branch.
                        if (list.ClassId != 3524) throw Fail;
                        if (!contextRead)
                        {
                            context = "clear-cross-section"; cross = RootCrossSection();
                            context = "clear-helper"; helper = Helper();
                            context = "clear-generator";
                            generator = cross is 0 or 1 or 2 || helper != null ? Generator(helper != null) : null;
                            contextRead = true;
                        }
                        for (int side = 0; side < 2; side++)
                        {
                            bool basicFaceSucceeded = cross is 0 or 1 or 2 || helper != null;
                            int[] keys = !basicFaceSucceeded ? [-1, -1, -1]
                                : cross is 0 or 2 && helper == null ? [3, side == 0 ? 3 : 1, 0] : [side + 1, -1, -1];
                            int id = -1; int? row = null;
                            // Legacy SWall returns an error for an unknown cross section
                            // without an elevation helper; getSideFaceId then returns -1.
                            if (basicFaceSucceeded && generator != null)
                            {
                                context = $"clear-face-history:{generator.QueueIndex}:{side}";
                                var table = generator.Tables.Where(t => t.Name == "m_faceHistTable").ToArray();
                                if (table.Length != 1 || table[0].Count == uint.MaxValue
                                    || table[0].Count != table[0].Entries.Count
                                    || table[0].Entries.Any(e => e.Keys.Count != 3)) throw Fail;
                                var matches = table[0].Entries.Select((e, i) => (e, i))
                                    .Where(x => x.e.Keys.SequenceEqual(keys)).ToArray();
                                // The native duplicate-key tie rule of the compact set is
                                // not replaced by a guessed first/last-row rule.
                                if (matches.Length > 1) throw Fail;
                                if (matches.Length == 1) { id = matches[0].e.Id; row = matches[0].i; }
                            }
                            sides.Add(new(side, keys, id, row));
                        }
                    }
                    else if (step.ClassId is not (150 or 365 or 500 or 590 or 677 or 977 or 982 or 1502
                        or 1621 or 2256 or 2262 or 3665 or 4036 or 4098 or 4137 or 4138 or 4147 or 4151
                        or 4153 or 4155 or 4165 or 4166 or 4167)) throw Fail;
                    callbacks.Add(new(phase, index, step.ClassId, step.Flags,
                        step.ClassId == 596 ? generator?.QueueIndex : null,
                        step.ClassId == 596 ? generator?.ClassId : null, sides));
                }
            failure = null;
            return new(list.OwnerId, list.ClassId, geometryCount, cross, helper, callbacks);
        }
        catch (DecodeFail) { failure = context; return null; }

        int RootCrossSection()
        {
            var fields = SerializationFields(list.ClassId) ?? throw Fail;
            if (!HasExactSchemaField(fields, 362, "m_wallCrossSection", 4, 0, null)) throw Fail;
            var cursor = new Cursor { D = input.FrameBytes, Pos = 14, End = list.RootEnd, RootClassId = list.ClassId };
            var queue = new List<(ushort, uint)>(); int? value = null;
            foreach (var field in fields)
                if (field.Declaring == 362 && field.Name == "m_wallCrossSection") value = ReadI32(cursor);
                else ReadField(cursor, field, queue, 0, null);
            if (cursor.Pos != list.RootEnd || value == null) throw Fail;
            return value.Value;
        }
        int? Helper()
        {
            if (references.CellListPointer.Handle == 0) return null;
            if (references.CellListPointer.QueueIndex is not int index) throw Fail;
            var cells = references.CellLists.Where(c => c.QueueIndex == index).ToArray();
            if (cells.Length != 1 || cells[0].Cells.Count == uint.MaxValue) throw Fail;
            foreach (var cell in cells[0].Cells.Members)
            {
                if (cell.QueueIndex is not int owned) throw Fail;
                // The primary slot 35 of class 4041 returns the unique cell of the ExtrusionHelper
                // (class 570), although its runtime/schema class is derived 4041.
                if (cell.ClassId == 4041)
                {
                    if (!input.LaterObjects.Any(n => n.QueueIndex == owned && n.ClassId == 4041)) throw Fail;
                    return owned;
                }
                if (cell.ClassId is not (308 or 311 or 445 or 877 or 953 or 954 or 1231 or 2075 or 4042)) throw Fail;
            }
            return null;
        }
        WallGeometryStepPrefix? Generator(bool extrusion)
        {
            foreach (var pointer in list.Phases[1].Members) // native GStepType2: form list
            {
                if (pointer.QueueIndex is not int index || !steps.TryGetValue(index, out var step)) throw Fail;
                if (IsSubclassOf(step.ClassId, extrusion ? 1502 : 500)) return step;
            }
            return null;
        }
    }

    /// <summary>Final null-node updates for callback 37 ids with debug mode off.
    /// Does not establish a cross-archive runtime identity or active faces.</summary>
    internal static OwnWallDebugOffClearedTable? ApplyOwnWallDebugOffClearing(OwnWallPhysicalTablePrefix prefix,
        OwnWallDebugOffClearIds ids, out string? failure)
    {
        failure = "clear-table-owner";
        if (prefix.OwnerId != ids.OwnerId || prefix.WallClassId != ids.ClassId || prefix.GeometryCount != ids.GeometryCount
            || prefix.GeometryCount < 0 || prefix.MaxId < -1 || prefix.MaxId > 100_000
            || prefix.Slots.Count != prefix.MaxId + 1
            || prefix.Slots.Where((s, i) => s.Persisted.Id != i || s.Node != null && s.Node.Tag != i).Any()
            || prefix.GeometryCount == 0 && ids.Callbacks.Count != 0) return null;
        var slots = prefix.Slots.ToArray(); var visits = new List<WallClearIdVisit>();
        foreach (var callback in ids.Callbacks)
            foreach (var side in callback.Sides)
            {
                if (side.Id == -1) continue; // native callback omits ONLY-1
                bool valid = side.Id >= 0 && side.Id <= prefix.MaxId;
                var previous = valid ? slots[side.Id].Node : null;
                if (valid) slots[side.Id] = slots[side.Id] with { Node = null };
                visits.Add(new(callback.StepQueueIndex, side.Side, side.Id, previous, valid ? "clear" : "out-of-range"));
            }
        failure = null;
        return new(prefix.OwnerId, prefix.WallClassId, prefix.MaxId, prefix.GeometryCount, visits, slots);
    }
}
