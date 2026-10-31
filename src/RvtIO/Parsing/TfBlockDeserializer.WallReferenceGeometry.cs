namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallCellListInput(int QueueIndex, uint Handle, int Start, int End,
        WallPhaseArray Cells);
    internal sealed record WallCurveDriverInput(int QueueIndex, uint Handle, int Start, int End,
        WallPhasePointer ControlData, WallPhasePointer Curve);
    internal sealed record WallReferenceFaceInput(int QueueIndex, uint Handle, int Start, int End,
        int Tag, int CategoryId, int SubcategoryId, uint Flags, WallPhasePointer FirstLoop,
        WallPhaseArray Regions, WallPhasePointer ForegroundFilling, WallPhasePointer BackgroundFilling,
        int CutType, uint FaceFlags, int RenderStyleId, WallPhasePointer Surface);
    internal sealed record OwnWallReferenceGeometryInputs(int OwnerId, ushort ClassId, int RootEnd,
        WallPhasePointer CellListPointer, WallPhasePointer CurveDriverPointer,
        WallPhaseArray ReferenceFaces, WallPhasePointer StepListPointer,
        uint ListOwnerReference, IReadOnlyList<WallPhaseDeferredObject> Objects, IReadOnlyList<WallCellListInput> CellLists,
        IReadOnlyList<WallCurveDriverInput> CurveDrivers, IReadOnlyList<WallReferenceFaceInput> Faces, int End);

    /// <summary>Own wall root and FIFO through its directly owned reference inputs.
    /// Retains reference Faces, not display/GRep faces, and raw driver/cell pointers.
    /// GInfo is exactly four inline words, never a sniffed pointer/flag pattern.
    /// Later curves, cells, surfaces and weak owner references remain unresolved;
    /// this is not a callback result, rebuilt table or active-face calculation.</summary>
    internal static OwnWallReferenceGeometryInputs? ExtractOwnWallReferenceGeometryInputs(
        byte[] data, int start, uint size, ushort classId, out string? failure)
    {
        failure = "frame";
        if (classId is not (3524 or 361) || start < 0 || start > data.Length - 16 || size < 4
            || size > int.MaxValue || (long)start + 16 + size > data.Length
            || BitConverter.ToUInt32(data, start + 8) != size
            || BitConverter.ToUInt16(data, start + 12) != classId
            || BitConverter.ToUInt32(data, start + 12 + (int)size) != size) return null;
        var rootFields = SerializationFields(classId);
        var driverFields = SerializationFields(4040);
        var cellFields = SerializationFields(653);
        var listFields = SerializationFields(2086);
        failure = "schema";
        if (rootFields == null || driverFields == null || listFields == null || !HasGElementGraphSchemas()
            || !HasOwnWallGeometryTableSchema()
            || !HasExactSchemaField(rootFields, 37, "m_docAccess", 14, 0, null)
            || !HasExactSchemaField(rootFields, 37, "m_id", 14, 0, 20)
            || !HasExactSchemaField(rootFields, 37, "m_geomSteps", 14, 1, null)
            || !HasExactSchemaField(rootFields, 37, "m_cellList", 14, 1, null)
            || !HasExactSchemaField(rootFields, 362, "m_pCurveDriver", 14, 1, null)
            || !HasExactSchemaField(rootFields, 362, "m_pRefFaces", 14, 81, null)
            || !HasExactSchemaField(driverFields, 87, "m_oControlData", 14, 1, null)
            || !HasExactSchemaField(driverFields, 87, "m_pCrv", 14, 1, null)
            || !HasExactSchemaField(listFields, 2086, "m_pElem", 14, 3, null)
            || cellFields is not { Count: 1 }
            || cellFields[0] is not { Declaring: 653, Name: "m_cells", Type: 14, Mods: 81,
                Ref: null, Cnt: null }) return null;
        var cursor = new Cursor { D = data, Pos = start + 14, End = start + 12 + (int)size,
            RootClassId = classId };
        var queue = new List<(ushort Cls, uint Id)>();
        WallPhasePointer? cells = null, driver = null, list = null;
        WallPhaseArray? references = null;
        int owner = 0;
        try
        {
            foreach (var field in rootFields)
            {
                failure = $"root:{field.Declaring}.{field.Name}";
                if (field.Declaring == 37 && field.Name == "m_docAccess")
                { if (ReadU32(cursor) != 1) return null; }
                else if (field.Declaring == 37 && field.Name == "m_id") owner = ReadI32(cursor);
                else if (field.Declaring == 37 && field.Name == "m_cellList") cells = Pointer();
                else if (field.Declaring == 37 && field.Name == "m_geomSteps") list = Pointer();
                else if (field.Declaring == 362 && field.Name == "m_pCurveDriver") driver = Pointer();
                else if (field.Declaring == 362 && field.Name == "m_pRefFaces") references = Array(field.Name);
                else ReadField(cursor, field, queue, 0, null);
                if (queue.Count > 100_000) throw Fail;
            }
            failure = "owner";
            if (owner <= 0 || owner != BitConverter.ToInt32(data, start)) return null;
            failure = "list-pointer";
            if (list is not { Handle: uint.MaxValue, ClassId: 2086, QueueIndex: int target }) return null;
            foreach (var pointer in references!.Members.Prepend(cells!).Prepend(driver!))
                if (pointer.QueueIndex is int owned) target = Math.Max(target, owned);
            int rootEnd = cursor.Pos - start;
            var prior = new List<WallPhaseDeferredObject>();
            var cellLists = new List<WallCellListInput>();
            var drivers = new List<WallCurveDriverInput>();
            var faces = new List<WallReferenceFaceInput>();
            uint ownerReference = 0;
            for (int index = 0; index <= target; index++)
            {
                var (cls, handle) = queue[index];
                failure = $"fifo:{index}:{cls}";
                int first = cursor.Pos - start;
                if (cls == 2086)
                {
                    foreach (var field in listFields)
                        if (field.Declaring == 2086 && field.Name == "m_pElem")
                        {
                            uint reference = ReadU32(cursor);
                            if (index == list.QueueIndex) ownerReference = reference;
                        }
                        else ReadField(cursor, field, queue, 1, null);
                }
                else if (cls == 653)
                {
                    var array = Array("m_cells");
                    cellLists.Add(new(index, handle, first, cursor.Pos - start, array));
                }
                else if (cls == 4040)
                {
                    WallPhasePointer? control = null, curve = null;
                    foreach (var field in driverFields)
                    {
                        failure = $"driver:{index}:{field.Declaring}.{field.Name}";
                        if (field.Declaring == 87 && field.Name == "m_oControlData") control = Pointer();
                        else if (field.Declaring == 87 && field.Name == "m_pCrv") curve = Pointer();
                        else ReadField(cursor, field, queue, 1, null);
                    }
                    drivers.Add(new(index, handle, first, cursor.Pos - start, control!, curve!));
                }
                else if (cls == 1589)
                {
                    // Exact Face/GFace/GNode order.
                    int tag = ReadI32(cursor), category = ReadI32(cursor), subcategory = ReadI32(cursor);
                    uint flags = ReadU32(cursor);
                    var loop = Pointer(); var regions = Array("m_faceRegions");
                    var foreground = Pointer(); var background = Pointer();
                    int cutType = ReadI32(cursor); uint faceFlags = ReadU32(cursor);
                    int style = ReadI32(cursor); var surface = Pointer();
                    faces.Add(new(index, handle, first, cursor.Pos - start, tag, category, subcategory,
                        flags, loop, regions, foreground, background, cutType, faceFlags, style, surface));
                }
                else if (cls == 2087)
                    ReadOwnWallGeometryTable(cursor, queue, start, index, handle);
                // Scope admission is explicit. In particular an arbitrary prior GNode
                // must not reach the generic reader's legacy GInfo sniffing path.
                else if (cls is 2887 or 2889 or 2886 or 2888 or 2288 or 2172 or 369)
                    ReadDeferredObject(cursor, cls, queue);
                else throw Fail;
                if (queue.Count > 100_000) throw Fail;
                prior.Add(new(index, cls, handle, first, cursor.Pos - start));
            }
            failure = null;
            return new(owner, classId, rootEnd, cells!, driver!, references!, list!, ownerReference, prior,
                cellLists, drivers, faces, cursor.Pos - start);
        }
        catch (DecodeFail) { return null; }

        WallPhasePointer Pointer()
        {
            int first = cursor.Pos, before = queue.Count;
            ReadPtr(cursor, queue);
            if (queue.Count > 100_000) throw Fail;
            uint handle = BitConverter.ToUInt32(data, first);
            ushort? cls = handle == 0 ? null : BitConverter.ToUInt16(data, first + 4);
            return new(handle, cls, queue.Count == before ? null : before, first - start, cursor.Pos - start);
        }

        WallPhaseArray Array(string name)
        {
            int first = cursor.Pos - start;
            uint count = ReadU32(cursor);
            var members = new List<WallPhasePointer>();
            if (count != uint.MaxValue)
            {
                if (count > 100_000 || count > (cursor.End - cursor.Pos) / 4) throw Fail;
                for (uint i = 0; i < count; i++) members.Add(Pointer());
            }
            return new(name, count, first, cursor.Pos - start, members);
        }
    }
}
