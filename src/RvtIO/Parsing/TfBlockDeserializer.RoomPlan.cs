namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    /// <summary>The room topology of a level (LevelRoomPlan.m_planTopology, class PlanTopology): the rooms the plan holds, the
    /// signed area of every circuit (a closed loop of room-bounding segments, in square feet) and the connected components
    /// of circuits with the circuit that encloses each of them (-1 when none does).</summary>
    internal sealed record RoomPlanTopology(
        IReadOnlyList<int> Rooms,
        IReadOnlyDictionary<int, double> CircuitAreas,
        IReadOnlyList<(int Outer, int Enclosing)> Components,
        IReadOnlyList<PlanSegment>? Segments = null,
        IReadOnlyDictionary<int, IReadOnlyList<SegmentKey>>? CircuitSides = null)
    {
        /// <summary>The area Revit gives a room: the area of its circuit less the area of every island, the connected
        /// component the circuit encloses (the outer circuit of such a component runs the other way round, so its signed
        /// area is negative).</summary>
        public double? RoomArea(int circuit)
        {
            if (!CircuitAreas.TryGetValue(circuit, out double area)) return null;
            foreach (var (outer, enclosing) in Components)
                if (enclosing == circuit) area += CircuitAreas[outer];
            return area;
        }
    }

    /// <summary>A DirectedSegmentKey: the room-bounding item (host or link instance id, link reference) and the two indices.</summary>
    internal readonly record struct SegmentKey(int Item, int LinkRef, int Major, int Sub);

    /// <summary>A PlanSegmentInfo with its key: the parameter span on the bounding element, the segment's area and the points of its polyline
    /// (feet, in the coordinates of the plan).</summary>
    internal sealed record PlanSegment(SegmentKey Key, double Param0, double Param1, double Area, double AreaOnRight,
        IReadOnlyList<(double X, double Y)> Points);

    private const int AnyReference = int.MinValue;

    private static bool SchemaIs(int classId, string name, bool prefixOnly,
        params (string Name, byte Type, byte Mods, int Ref)[] expected)
    {
        if (!Schemas.TryGetValue(classId, out var schema) || schema.Name != name
            || schema.Fields.Count < expected.Length || (!prefixOnly && schema.Fields.Count != expected.Length))
            return false;
        for (int i = 0; i < expected.Length; i++)
        {
            var field = schema.Fields[i];
            if (field.Name != expected[i].Name || field.Type != expected[i].Type || field.Mods != expected[i].Mods
                || (expected[i].Ref != AnyReference && field.Ref != (expected[i].Ref == 0 ? (int?)null : expected[i].Ref)))
                return false;
        }
        return true;
    }

    /// <summary>The layouts the plan reader below follows, as the r22 schema declares them. The key of a segment pair
    /// (DirectedSegmentKey: RoomBoundingItem = two ElementIds, then two ints), a CircuitId (one int) and an Envelope (two XY
    /// corners) are inline values whose recovered references are not their layouts.</summary>
    private static bool RoomPlanSchemasMatch() =>
        SchemaIs(2306, "LevelRoomPlan", true) && SchemaIs(2980, "PlanTopology", true,
            ("m_elevOffset", 7, 0, 0), ("m_topOffset", 7, 0, 0), ("m_botOffset", 7, 0, 0), ("m_whichElev", 4, 0, 0),
            ("m_levelId", 14, 0, 20), ("m_levelRoomPlanId", 14, 0, 20), ("m_nextCircuitId", 14, 0, 182),
            ("m_topBotOffsetsSet", 1, 0, 0), ("m_segments", 14, 80, 2977), ("m_circuits", 14, 80, 2971),
            ("m_components", 14, 80, 2972), ("m_rooms", 14, 80, 20))
        && SchemaIs(2977, "PlanSegmentInfoPair", false, ("m_key", 14, 0, 0), ("m_info", 14, 0, 2976))
        && SchemaIs(2976, "PlanSegmentInfo", false, ("m_param0", 7, 0, 0), ("m_param1", 7, 0, 0), ("m_envelope", 14, 0, 524),
            ("m_area", 7, 0, 0), ("m_areaOnRight", 7, 0, 0), ("m_points", 13, 80, 0))
        && SchemaIs(2971, "PlanCircuit", false, ("m_area", 7, 0, 0), ("m_envelope", 14, 0, 524), ("m_id", 14, 0, 182),
            ("m_sides", 14, 80, 1150))
        && SchemaIs(2972, "PlanConnectedComponent", false, ("m_circuits", 14, 80, 182), ("m_outerCircuit", 14, 0, 182),
            ("m_enclosingCircuit", 14, 0, 182))
        && SchemaIs(1150, "DirectedSegmentKey", false, ("m_item", 14, 0, 180), ("m_majorIndex", 4, 0, 0), ("m_subIndex", 4, 0, 0))
        && SchemaIs(180, "RoomBoundingItem", false, ("m_linkInstOrHostId", 14, 0, 20), ("m_linkRef", 14, 0, AnyReference))
        && SchemaIs(182, "CircuitId", false, ("m_id", 4, 0, AnyReference))
        && SchemaIs(524, "Envelope", false, ("m_corners", 13, 16, AnyReference))
        && Schemas[524].Fields[0].Cnt == 2;

    /// <summary>Reads the room topology of an owned LevelRoomPlan root: the root fields and the objects they queue before the
    /// topology are consumed by the generic grammar, the topology itself is read field by field up to its room list. The
    /// plan must name itself (m_levelRoomPlanId), its circuits must be distinct and its components must refer to them.</summary>
    internal static bool TryExtractExactRoomPlan(
        byte[] data, int start, uint size, ushort classId, out RoomPlanTopology? plan)
    {
        plan = null;
        long end = (long)start + 16 + size;
        if (classId != 2306 || start < 0 || size < 4 || end > data.Length
            || start > data.Length - 16
            || BitConverter.ToUInt32(data, start + 8) != size
            || BitConverter.ToUInt16(data, start + 12) != classId
            || BitConverter.ToUInt32(data, (int)end - 4) != size
            || !RoomPlanSchemasMatch())
            return false;
        var fields = SerializationFields(classId);
        if (fields == null || !HasExactSchemaField(fields, 2306, "m_planId", 14, 0, 20)
            || !HasExactSchemaField(fields, 2306, "m_planTopology", 14, 1, null))
            return false;
        var cursor = new Cursor { D = data, Pos = start + 14, End = (int)end - 4, RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        var captured = new NativeParams();
        try
        {
            foreach (var field in fields) ReadField(cursor, field, queue, 0, captured);
            if (!captured.Fields.TryGetValue("m_id", out int owner)
                || owner <= 0 || (uint)owner != BitConverter.ToUInt32(data, start))
                return false;
            int topology = queue.FindIndex(item => item.Cls == 2980);
            if (topology < 0 || queue.FindLastIndex(item => item.Cls == 2980) != topology) return false;
            for (int i = 0; i < topology; i++) ReadDeferredObject(cursor, queue[i].Cls, queue);
            plan = ReadRoomPlanTopology(cursor, owner);
            return plan != null;
        }
        catch (DecodeFail) { return false; }
    }

    private static RoomPlanTopology? ReadRoomPlanTopology(Cursor c, int owner)
    {
        for (int i = 0; i < 3; i++) ReadFiniteF64(c);
        ReadI32(c); // m_whichElev
        ReadI32(c); // m_levelId
        if (ReadI32(c) != owner) return null; // m_levelRoomPlanId
        ReadI32(c); // m_nextCircuitId
        if (!c.Take(1) || c.D[c.Pos - 1] > 1) throw Fail; // m_topBotOffsetsSet

        // m_segments: key (16 bytes), two parameters, envelope, area, area on the right, points (XY pairs).
        int segments = ReadCount(c, 84);
        var segmentList = new List<PlanSegment>(segments);
        for (int i = 0; i < segments; i++)
        {
            if (!c.Take(80)) throw Fail;
            int start = c.Pos - 80;
            var key = new SegmentKey(BitConverter.ToInt32(c.D, start), BitConverter.ToInt32(c.D, start + 4),
                BitConverter.ToInt32(c.D, start + 8), BitConverter.ToInt32(c.D, start + 12));
            double param0 = BitConverter.ToDouble(c.D, start + 16), param1 = BitConverter.ToDouble(c.D, start + 24);
            double area = BitConverter.ToDouble(c.D, start + 64), areaOnRight = BitConverter.ToDouble(c.D, start + 72);
            int points = ReadCount(c, 16);
            if (!c.Take(16 * points)) throw Fail;
            var xy = new (double X, double Y)[points];
            for (int k = 0; k < points; k++)
                xy[k] = (BitConverter.ToDouble(c.D, c.Pos - 16 * (points - k)), BitConverter.ToDouble(c.D, c.Pos - 16 * (points - k) + 8));
            segmentList.Add(new PlanSegment(key, param0, param1, area, areaOnRight, xy));
        }

        int circuitCount = ReadCount(c, 48);
        var areas = new Dictionary<int, double>(circuitCount);
        var circuitSides = new Dictionary<int, IReadOnlyList<SegmentKey>>(circuitCount);
        for (int i = 0; i < circuitCount; i++)
        {
            double area = ReadFiniteF64(c);
            if (!c.Take(32)) throw Fail; // m_envelope
            int id = ReadI32(c);
            if (id < 0 || !areas.TryAdd(id, area)) return null;
            int sides = ReadCount(c, 16);
            if (!c.Take(16 * sides)) throw Fail;
            var keys = new SegmentKey[sides];
            for (int k = 0; k < sides; k++)
            {
                int at = c.Pos - 16 * (sides - k);
                keys[k] = new SegmentKey(BitConverter.ToInt32(c.D, at), BitConverter.ToInt32(c.D, at + 4),
                    BitConverter.ToInt32(c.D, at + 8), BitConverter.ToInt32(c.D, at + 12));
            }
            circuitSides[id] = keys;
        }

        int componentCount = ReadCount(c, 12);
        var components = new List<(int Outer, int Enclosing)>(componentCount);
        for (int i = 0; i < componentCount; i++)
        {
            int members = ReadCount(c, 4);
            for (int j = 0; j < members; j++)
                if (!areas.ContainsKey(ReadI32(c))) return null;
            int outer = ReadI32(c);
            int enclosing = ReadI32(c);
            if (!areas.ContainsKey(outer) || (enclosing != -1 && !areas.ContainsKey(enclosing))) return null;
            components.Add((outer, enclosing));
        }

        int roomCount = ReadCount(c, 4);
        var rooms = new List<int>(roomCount);
        for (int i = 0; i < roomCount; i++) rooms.Add(ReadI32(c));
        if (rooms.Any(room => room <= 0) || rooms.Distinct().Count() != rooms.Count) return null;
        return new RoomPlanTopology(rooms, areas, components, segmentList, circuitSides);
    }

    private static double ReadFiniteF64(Cursor c)
    {
        double value = ReadF64(c);
        if (!double.IsFinite(value)) throw Fail;
        return value;
    }

    /// <summary>The element count of a counted container, which holds at least <paramref name="minimumBytes"/> bytes per
    /// element; 0xFFFFFFFF is the empty container.</summary>
    private static int ReadCount(Cursor c, int minimumBytes)
    {
        uint count = ReadU32(c);
        if (count == 0xFFFFFFFF) return 0;
        if (count > (uint)((c.End - c.Pos) / minimumBytes)) throw Fail;
        return (int)count;
    }
}
