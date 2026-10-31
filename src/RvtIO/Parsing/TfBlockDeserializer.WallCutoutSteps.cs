namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record OwnWallCutoutStep(int PhaseIndex, int QueueIndex, ushort ClassId,
        int Start, int End, int CutSomething, int InstanceId, int InfillBefore, int InfillAfter,
        byte IsTrueBoolean, byte UseUnattachedVoids, byte RemovedWholeGeometry,
        IReadOnlyList<byte>? TrueBooleanArray, int GeneratorId = 0,
        int? OtherWallId = null, int? OtherGeneratorId = null);

    internal static int? ExtractOwnWallCrossSection(byte[] data, int start, uint size, ushort classId)
    {
        if (classId is not (3524 or 361)
            || !TryExtractOwnElementClass(data, start, size, classId, out _)) return null;
        var fields = SerializationFields(classId);
        if (fields == null || !HasExactSchemaField(fields, 362, "m_wallCrossSection", 4, 0, null)) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = start + 12 + (int)size, RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        try
        {
            foreach (var field in fields)
            {
                if (field.Declaring == 362 && field.Name == "m_wallCrossSection") return ReadI32(cursor);
                ReadField(cursor, field, queue, 0, null);
                if (queue.Count > 100_000) return null;
            }
        }
        catch (DecodeFail) { return null; }
        return null;
    }

    /// <summary>WallCutoutGStepBase inputs in getGList(4) order.
    /// No active geometry, nested instance or per-host CutIdxSpec claim.</summary>
    internal static List<OwnWallCutoutStep>? ExtractOwnWallCutoutSteps(
        byte[] data, int start, uint size, ushort classId, out string? failure)
    {
        var history = ExtractOwnWallGeometryHistories(data, start, size, classId, out failure);
        if (history is not { Complete: true }) return null;
        var fields = SerializationFields(1793)?.Where(field => field.Declaring == 1793).ToArray();
        string[] ids = ["m_bCutSomething", "m_instId", "m_infillIdBefore", "m_infillIdAfter"];
        string[] flags = ["m_bIsTrueBoolean", "m_bUseUnattachedVoids", "m_removedWholeGeometry"];
        if (fields is not { Length: 8 }
            || ids.Where((name, index) => fields[index].Name != name
                || fields[index].Mods != 0 || fields[index].Cnt != null
                || (index == 0 ? fields[index] is not { Type: 4, Ref: null }
                    : fields[index] is not { Type: 14, Ref: 20 })).Any()
            || flags.Where((name, index) => fields[index + 4] is not { Type: 1, Mods: 0, Ref: null, Cnt: null }
                || fields[index + 4].Name != name).Any()
            || fields[7] is not { Name: "m_bIsTrueBooleanArr", Type: 1, Mods: 80, Ref: null, Cnt: null })
        { failure = "cutout-schema"; return null; }
        var steps = history.Steps.ToDictionary(step => step.QueueIndex);
        var result = new List<OwnWallCutoutStep>();
        var phase = history.List.Phases[3];
        for (int index = 0; index < phase.Members.Count; index++)
        {
            var pointer = phase.Members[index];
            if (pointer.QueueIndex is not int queueIndex || !steps.TryGetValue(queueIndex, out var step))
            { failure = "cutout-pointer"; return null; }
            if (!IsSubclassOf(step.ClassId, 1793)) continue;
            if (!ExactWallGeomStepVersions.Contains((step.ClassId, step.Version)))
            { failure = $"cutout-version:{step.ClassId}:{step.Version}"; return null; }
            var cursor = new Cursor { D = data, Pos = start + step.End, End = start + 12 + (int)size };
            try
            {
                int cut = ReadI32(cursor), instance = ReadI32(cursor);
                int before = ReadI32(cursor), after = ReadI32(cursor);
                if (!cursor.Take(3)) throw Fail;
                byte truth = data[cursor.Pos - 3], unattached = data[cursor.Pos - 2], removed = data[cursor.Pos - 1];
                uint count = ReadU32(cursor);
                byte[]? values = null;
                if (count != uint.MaxValue)
                {
                    if (count > 100_000 || !cursor.Take((int)count)) throw Fail;
                    values = data.AsSpan(cursor.Pos - (int)count, (int)count).ToArray();
                }
                int? otherWall = null, otherGenerator = null;
                if (step.ClassId == 4155)
                {
                    var shadowFields = SerializationFields(4155);
                    if (shadowFields == null
                        || !HasExactSchemaField(shadowFields, 4155, "m_otherWallId", 14, 0, 20)
                        || !HasExactSchemaField(shadowFields, 4155, "m_ggId", 4, 0, null))
                    { failure = "shadow-schema"; return null; }
                    otherWall = ReadI32(cursor);
                    otherGenerator = ReadI32(cursor);
                }
                result.Add(new(index, queueIndex, step.ClassId, step.End, cursor.Pos - start,
                    cut, instance, before, after, truth, unattached, removed, values,
                    step.Id, otherWall, otherGenerator));
            }
            catch (DecodeFail) { failure = $"cutout-tail:{queueIndex}"; return null; }
        }
        failure = null;
        return result;
    }

    // IntfIFC passes both true/false-kind flags: only reverse phase order and instance ID select the step.
    internal static OwnWallCutoutStep? FindOwnWallCutoutStep(
        IReadOnlyList<OwnWallCutoutStep> steps, int instanceId)
    {
        if (instanceId <= 0) return null;
        for (int index = steps.Count - 1; index >= 0; index--)
            if (steps[index].InstanceId == instanceId) return steps[index];
        return null;
    }
}
