namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record GElementGInfo(int Tag, int Control, int RenderStyleId, uint Flags, int Start, int End);
    internal sealed record GElementPointer(uint Handle, ushort? ClassId, int? QueueIndex, int Start, int End);
    internal sealed record GElementArray(string Name, uint Count, int Start, int End,
        IReadOnlyList<GElementPointer> Members);
    internal sealed record GElementPriorNode(int QueueIndex, ushort ClassId, uint Handle, int Start, int End,
        GElementGInfo GInfo, GElementArray? SubNodes, GElementArray? Conditions, GElementPointer? InstanceInfo);
    internal sealed record GElementGeometry(int QueueIndex, int Start, int End, GElementGInfo GInfo,
        GElementArray Faces, int Flags, int GeometryTag, int TessellationType, int TessellationVersion,
        GElementArray Edges, GElementArray SharedSurfaceInfo);
    internal sealed record OwnGElementGeometryPrefix(int OwnerId, uint OpaqueHeader, int RootEnd,
        GElementGInfo GInfo, IReadOnlyList<long> BoxBits, IReadOnlyList<long> TightBoxBits,
        int ElementType, uint Flags, GElementArray SubNodes, GElementPointer? SelectedPointer,
        IReadOnlyList<GElementPriorNode> PriorNodes, GElementGeometry? Geometry, int End);

    /// <summary>Exact own GElement root and FIFO through its first direct GBRep/Geometry. Known non-BRep kinds are
    /// GGroup/GFilter/GComponentRef. An unresolved or unsupported preceding kind is a failure, not an absent geometry.
    /// This is a serialized prefix, not a complete node graph, active-face or extrusion predicate.</summary>
    internal static OwnGElementGeometryPrefix? ExtractOwnGElementGeometryPrefix(
        byte[] data, int start, uint size, ushort classId, out string? failure)
    {
        failure = "frame";
        if (classId != 2007 || start < 0 || start > data.Length - 16 || size < 4 || size > int.MaxValue
            || (long)start + 16 + size > data.Length
            || BitConverter.ToUInt32(data, start + 8) != size
            || BitConverter.ToUInt16(data, start + 12) != classId
            || BitConverter.ToUInt32(data, start + 12 + (int)size) != size) return null;
        failure = "owner";
        int owner = BitConverter.ToInt32(data, start);
        if (owner <= 0) return null;
        failure = "schema";
        if (!HasGElementGeometryPrefixSchemas()) return null;
        var c = new Cursor { D = data, Pos = start + 14, End = start + 12 + (int)size, RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        try
        {
            failure = "root:m_GInfo";
            var info = Info();
            failure = "root:m_subNodes";
            var subNodes = Array("m_subNodes");
            failure = "root:m_bBox";
            var box = Box();
            failure = "root:m_tightbBox";
            var tight = Box();
            failure = "root:m_gElemType";
            int elementType = ReadI32(c);
            failure = "root:m_flags";
            uint flags = ReadU32(c);
            int rootEnd = c.Pos - start;

            GElementPointer? selected = null;
            for (int i = 0; i < subNodes.Members.Count; i++)
            {
                var pointer = subNodes.Members[i];
                if (pointer.Handle == 0) continue;
                failure = $"subnode-kind:{i}:{pointer.ClassId}";
                // Do not skip external pointers, hand GInfo or unknown runtime kinds:
                // one may precede the first GBRep returned by the accessor.
                if (pointer.QueueIndex == null) return null;
                if (pointer.ClassId == 2092) { selected = pointer; break; }
                if (pointer.ClassId is not (2009 or 2015 or 1991)) return null;
            }
            var prior = new List<GElementPriorNode>();
            GElementGeometry? geometry = null;
            if (selected?.QueueIndex is int target)
            {
                for (int index = 0; index < target; index++)
                {
                    var node = queue[index];
                    failure = $"fifo:{index}:{node.Cls}";
                    if (node.Cls is not (2009 or 2015 or 1991)) return null;
                    int first = c.Pos - start;
                    var nodeInfo = Info();
                    GElementArray? children = null, conditions = null;
                    GElementPointer? instance = null;
                    if (node.Cls is 2009 or 2015) children = Array("m_subNodes");
                    if (node.Cls == 2015) conditions = Array("m_oConditions");
                    if (node.Cls == 1991) instance = Pointer();
                    prior.Add(new(index, node.Cls, node.Id, first, c.Pos - start,
                        nodeInfo, children, conditions, instance));
                }
                failure = $"geometry:{target}:2092";
                int geometryStart = c.Pos - start;
                var geometryInfo = Info();
                var faces = Array("m_pFaces");
                int geometryFlags = ReadI32(c), tag = ReadI32(c);
                // Utility.TessEpsCntrl is inline: two signed32 fields.
                // The scalar self-ref metadata on its version is not recursion.
                int tessellationType = ReadI32(c), tessellationVersion = ReadI32(c);
                var edges = Array("m_pEdges");
                var shared = Array("m_sharedSurfInfo");
                geometry = new(target, geometryStart, c.Pos - start, geometryInfo,
                    faces, geometryFlags, tag, tessellationType, tessellationVersion, edges, shared);
            }
            failure = null;
            return new(owner, BitConverter.ToUInt32(data, start + 4), rootEnd, info, box, tight,
                elementType, flags, subNodes, selected, prior, geometry, c.Pos - start);
        }
        catch (DecodeFail) { return null; }

        GElementGInfo Info()
        {
            int first = c.Pos - start;
            return new(ReadI32(c), ReadI32(c), ReadI32(c), ReadU32(c), first, c.Pos - start);
        }
        long[] Box()
        {
            if (!c.Take(48)) throw Fail;
            int first = c.Pos - 48;
            return Enumerable.Range(0, 6).Select(i => BitConverter.ToInt64(data, first + i * 8)).ToArray();
        }
        GElementPointer Pointer()
        {
            int first = c.Pos, before = queue.Count;
            ReadPtr(c, queue);
            if (queue.Count > 100_000) throw Fail;
            uint handle = BitConverter.ToUInt32(data, first);
            ushort? pointerClass = handle == 0 ? null : BitConverter.ToUInt16(data, first + 4);
            return new(handle, pointerClass, queue.Count == before ? null : before,
                first - start, c.Pos - start);
        }
        GElementArray Array(string name)
        {
            int first = c.Pos - start;
            uint count = ReadU32(c);
            var members = new List<GElementPointer>();
            if (count != uint.MaxValue)
            {
                if (count > 100_000 || count > (uint)((c.End - c.Pos) / 4)) throw Fail;
                for (uint i = 0; i < count; i++) members.Add(Pointer());
            }
            return new(name, count, first, c.Pos - start, members);
        }
    }

    private static bool HasGElementGeometryPrefixSchemas()
    {
        TfField F(int declaring, string name, byte type, byte mods = 0, int? reference = null, int? count = null)
            => new(name, type, mods, reference, count, declaring);
        TfField[] node = [F(1246, "m_GInfo", 14)];
        TfField[] group = [..node, F(2009, "m_subNodes", 14, 81)];
        TfField[] rep = [..group, F(2008, "m_bBox", 13, 16, count: 2),
            F(2008, "m_tightbBox", 13, 16, count: 2), F(2008, "m_gElemType", 4), F(2008, "m_flags", 5)];
        TfField[] brep = [..node, F(1978, "m_pFaces", 14, 81)];
        (ushort Class, int? Base, TfField[] Fields)[] expected =
        [
            (1246, null, node), (2009, 1246, group), (2008, 2009, rep), (2007, 2008, rep),
            (2015, 2009, [..group, F(2015, "m_oConditions", 14, 81)]),
            (1991, 1246, [..node, F(1991, "m_instanceInfo", 14, 1)]), (1978, 1246, brep),
            (2092, 1978, [..brep, F(2092, "m_flags", 4), F(2092, "m_geometryTag", 4),
                F(2092, "m_tessEpsCntrl", 14, reference: 1969), F(2092, "m_pEdges", 14, 81),
                F(2092, "m_sharedSurfInfo", 14, 81)]),
            (1969, null, [F(1969, "m_type", 4), F(1969, "m_TessEpsCntrlVersion", 4, reference: 1969)])
        ];
        return expected.All(x => Schemas.TryGetValue(x.Class, out var schema) && schema.Base == x.Base
            && SerializationFields(x.Class) is { } fields && fields.SequenceEqual(x.Fields));
    }
}
