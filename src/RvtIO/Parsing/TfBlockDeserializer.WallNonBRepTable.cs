namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallNonBRepNode(int QueueIndex, ushort ClassId, int Tag, uint Flags,
        int Start, int End);
    internal sealed record WallNonBRepCallback(int PhaseIndex, int StepQueueIndex, ushort ClassId,
        int Flags, bool Skipped, IReadOnlyList<WallNonBRepNode?> Nodes);
    internal sealed record WallNonBRepSlot(WallLogicalGeomSlot Persisted, WallNonBRepNode? Node);
    internal sealed record OwnWallNonBRepTablePrefix(int OwnerId, int MaxId,
        IReadOnlyList<WallNonBRepCallback> Callbacks, IReadOnlyList<WallNonBRepSlot> Slots);

    /// <summary>Fresh own archive, all six ordered callback 36 lists and the non-BRep collision rule. Runtime pointers
    /// start empty; generator ids keep their persisted provenance. This stops before the GRep face/edge updates and
    /// the callback 37 clearing, and must not be used as a final or active table.</summary>
    internal static OwnWallNonBRepTablePrefix? ExtractOwnWallNonBRepTablePrefix(
        byte[] data, int start, uint size, ushort classId, out string? failure)
    {
        var input = ExtractOwnWallCompleteInputs(data, start, size, classId, out failure);
        if (input == null) return null;
        var table = SelectOwnWallGeometryTableAddressing(input, out failure);
        if (table == null) return null;
        var references = ExtractOwnWallReferenceGeometryInputs(data, start, size, classId, out failure);
        if (references == null) return null;
        var history = input.Histories.Histories;
        failure = "non-brep-owner";
        if (history.List.OwnerReference != 2 || history.Steps.Any(s => s.OwnerReference != 2)
            || references.OwnerId != table.OwnerId || references.ListOwnerReference != 2) return null;
        var steps = history.Steps.ToDictionary(s => s.QueueIndex);
        var late = input.LaterObjects.ToDictionary(n => n.QueueIndex);
        var faces = references.Faces.ToDictionary(n => n.QueueIndex);
        var callbacks = new List<WallNonBRepCallback>();
        var current = new WallNonBRepNode?[table.Slots.Count];
        string context = "non-brep";
        try
        {
            for (int phase = 0; phase < history.List.Phases.Count; phase++)
                foreach (var pointer in history.List.Phases[phase].Members)
                {
                    context = $"callback:{phase}:{pointer.QueueIndex}";
                    if (pointer.QueueIndex is not int index || !steps.TryGetValue(index, out var step)
                        || pointer.ClassId != step.ClassId) throw Fail;
                    bool skipped = (step.Flags & 0x100000) != 0;
                    var nodes = new List<WallNonBRepNode?>(); // reset before EVERY callback
                    if (!skipped)
                        switch (step.ClassId)
                        {
                            case 4153:
                                context = $"reference-faces:{index}";
                                if (references.ReferenceFaces.Count == uint.MaxValue) throw Fail;
                                foreach (var face in references.ReferenceFaces.Members) nodes.Add(Node(face, false));
                                context = $"path-curve:{index}";
                                if (references.CurveDriverPointer.QueueIndex is not int driverIndex) throw Fail;
                                var drivers = references.CurveDrivers.Where(d => d.QueueIndex == driverIndex).ToArray();
                                if (drivers.Length != 1 || drivers[0].ControlData.Handle != 0) throw Fail;
                                // Non-null TempSpline control data is a separate branch.
                                nodes.Add(Node(drivers[0].Curve, true));
                                break;
                            case 365:
                                context = $"axis-cell-list:{index}";
                                if (references.CellListPointer.Handle == 0) break;
                                if (references.CellListPointer.QueueIndex is not int cellIndex) throw Fail;
                                var lists = references.CellLists.Where(c => c.QueueIndex == cellIndex).ToArray();
                                if (lists.Length != 1 || lists[0].Cells.Count == uint.MaxValue) throw Fail;
                                // getUniqueCell returns the first exact runtime-class match,
                                // not an isKindOf search, uniqueness check or deduplication.
                                foreach (var cell in lists[0].Cells.Members)
                                {
                                    if (cell.QueueIndex == null) throw Fail;
                                    if (cell.ClassId != 953) continue;
                                    context = $"axis-cell:{cell.QueueIndex}";
                                    if (!late.TryGetValue(cell.QueueIndex.Value, out var axisCell)
                                        || axisCell.ClassId != 953) throw Fail;
                                    var field = Field(axisCell, 953, "m_oAxis");
                                    var bytes = field.Bytes;
                                    if (bytes.Length == 4 && BitConverter.ToUInt32(bytes) == 0
                                        && field.QueuedObjects.Count == 0) nodes.Add(null);
                                    else
                                    {
                                        if (bytes.Length != 6 || field.QueuedObjects.Count != 1
                                            || BitConverter.ToUInt16(bytes, 4) != 1736) throw Fail;
                                        nodes.Add(Node(new(BitConverter.ToUInt32(bytes), 1736,
                                            field.QueuedObjects[0], field.Start, field.End), true));
                                    }
                                    break;
                                }
                                break;
                            case 150 or 500 or 590 or 596 or 677 or 977 or 982 or 1502 or 1621
                                or 2256 or 2262 or 3665 or 4036 or 4098 or 4137 or 4138 or 4147
                                or 4151 or 4155 or 4165 or 4166 or 4167:
                                break; // source-bound empty base callback or exact no-op
                            default: throw Fail;
                        }
                    callbacks.Add(new(phase, index, step.ClassId, step.Flags, skipped, nodes));
                    foreach (var node in nodes)
                    {
                        if (node == null || node.Tag < 0 || node.Tag > table.MaxId) continue;
                        var old = current[node.Tag];
                        if (old != null && IsCurve(node) && !IsCurve(old)) continue;
                        current[node.Tag] = node;
                    }
                }
            failure = null;
            return new(table.OwnerId, table.MaxId, callbacks,
                table.Slots.Select((slot, index) => new WallNonBRepSlot(slot, current[index])).ToArray());
        }
        catch (DecodeFail) { failure = context; return null; }

        static bool IsCurve(WallNonBRepNode node) => node.ClassId is 1736 or 1974;
        static WallCompleteInputField Field(WallCompleteInputObject node, int declaring, string name)
        {
            var fields = node.Fields.Where(f => f.Declaring == declaring && f.Name == name).ToArray();
            if (fields.Length != 1 || fields[0].Bytes.Length != fields[0].End - fields[0].Start) throw Fail;
            return fields[0];
        }
        WallNonBRepNode? Node(WallPhasePointer pointer, bool curve)
        {
            if (pointer.Handle == 0) return null;
            if (pointer.QueueIndex is not int index) throw Fail;
            if (!curve)
            {
                if (pointer.ClassId != 1589 || !faces.TryGetValue(index, out var face)) throw Fail;
                return new(index, 1589, face.Tag, face.Flags, face.Start, face.End);
            }
            if (pointer.ClassId is not (1736 or 1974) || !late.TryGetValue(index, out var node)
                || node.ClassId != pointer.ClassId) throw Fail;
            var info = Field(node, 1246, "m_GInfo");
            if (info.Bytes.Length != 16 || info.QueuedObjects.Count != 0) throw Fail;
            return new(index, node.ClassId, BitConverter.ToInt32(info.Bytes),
                BitConverter.ToUInt32(info.Bytes, 12), node.Start, node.End);
        }
    }
}
