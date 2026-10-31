using RvtIO.Internal;
using RvtIO.Model;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class WallNonBRepTableTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void OwnDriverLineUsesNamedFieldsNotGInfoSignaturesOrElementRecords(int cls)
    {
        double[] values = [-2, 3, 17, -29, 40, 0, -2, 0];
        byte[] bytes = Block(root: (ushort)cls, pathValues: values, nodeFlags: 0xdeadbeef, pathTag: 37);
        var line = TfBlockDeserializer.ExtractOwnWallLocationLine(bytes, 0,
            BitConverter.ToUInt32(bytes, 8), (ushort)cls, out var failure)!;
        Assert.Null(failure); Assert.NotNull(line);
        Assert.Equal(values[..2], line.EndParams); Assert.Equal(values[2..5], line.OriginPoint);
        Assert.Equal(values[5..], line.DirectionVector); Assert.False(line.IsArc);
        var state = new ParsingState();
        RecordLine(state, bytes, 0, (ushort)cls, 100);
        var model = new RevitFileInfo(); ElementBuilder.BuildElemTableElement(100, 0, state, model);
        var location = Assert.Single(model.Elements).Location!;
        Assert.NotNull(location); Assert.Equal("Line", location.CurveType);
        Assert.Equal(new double[] { 17, -25, 40 }, location.StartPoint);
        Assert.Equal(new double[] { 17, -35, 40 }, location.EndPoint); Assert.Equal(10, location.Length);
    }

    [Theory]
    [InlineData("control")] [InlineData("null")] [InlineData("external")] [InlineData("arc")]
    [InlineData("footer")] [InlineData("nan")] [InlineData("infinity")]
    [InlineData("empty")] [InlineData("zero-direction")] [InlineData("overflow")]
    [InlineData("length-overflow")] [InlineData("collapsed-endpoints")]
    public void OwnLineDoesNotSubstituteAnUnrelatedCurveForUnconfirmedDriverInput(string mutation)
    {
        double[] values = [0, 4, 17, -29, 40, 1, 0, 0];
        if (mutation == "nan") values[2] = double.NaN;
        if (mutation == "infinity") values[0] = double.PositiveInfinity;
        if (mutation == "empty") values[1] = values[0];
        if (mutation == "zero-direction") values[5] = 0;
        if (mutation == "overflow") values[5] = double.MaxValue;
        if (mutation == "length-overflow") values[5] = 1e200;
        if (mutation == "collapsed-endpoints") { values[2] = 1e100; values[1] = 1; }
        byte[] bytes = Block(axisTags: [9], controlData: mutation == "control", nullPath: mutation == "null",
            externalPath: mutation == "external", pathClass: mutation == "arc" ? (ushort)1974 : (ushort)1736,
            pathValues: mutation == "arc" ? null : values);
        if (mutation == "footer") bytes[^1] ^= 1;
        Assert.Null(TfBlockDeserializer.ExtractOwnWallLocationLine(bytes, 0,
            BitConverter.ToUInt32(bytes, 8), 3524, out var failure)); Assert.NotNull(failure);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DuplicateWallRootsInvalidateTheLineRegardlessOfOrder(bool malformedFirst)
    {
        byte[] valid = Block(pathValues: [0, 4, 17, -29, 40, 1, 0, 0]);
        byte[] malformed = (byte[])valid.Clone(); malformed[^1] ^= 1;
        var state = new ParsingState();
        foreach (byte[] bytes in malformedFirst ? new[] { malformed, valid } : new[] { valid, malformed })
            RecordLine(state, bytes, 0, 3524, 100);
        Assert.Equal(2, state.OwnedElementClassRoots[100].BlockCount); Assert.Empty(state.OwnWallLocationLines);
        var model = new RevitFileInfo(); ElementBuilder.BuildElemTableElement(100, 0, state, model);
        Assert.Null(Assert.Single(model.Elements).Location);
    }

    [Fact]
    public void NonzeroFrameOffsetWorksButMismatchedCaptureOwnerCannotSupplyALine()
    {
        byte[] bytes = Block(pathValues: [0, 4, 17, -29, 40, 1, 0, 0]);
        byte[] segment = new byte[bytes.Length + 20]; bytes.CopyTo(segment, 9);
        Assert.NotNull(TfBlockDeserializer.ExtractOwnWallLocationLine(segment, 9,
            BitConverter.ToUInt32(bytes, 8), 3524, out var failure)); Assert.Null(failure);
        var state = new ParsingState();
        RecordLine(state, segment, 9, 3524, 101);
        Assert.Empty(state.OwnWallLocationLines);
    }

    private static void RecordLine(ParsingState state, byte[] data, int start, ushort cls, uint owner)
    {
        state.ElemRecOwningElementIds ??= new(); state.ElemRecOwningElementIds[owner] = -1;
        uint size = BitConverter.ToUInt32(data, start + 8);
        StairComponentSource.Record(state, data, start, size, cls, owner);
        RvtFileReader.RecordWallLocationLine(state, data, start, size, cls, owner);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AConflictingSemanticRootInvalidatesTheLineInEitherOrder(bool conflictFirst)
    {
        byte[] bytes = Block(pathValues: [0, 4, 17, -29, 40, 1, 0, 0]);
        var state = new ParsingState();
        foreach (ushort cls in conflictFirst ? new ushort[] { 1794, 3524 } : new ushort[] { 3524, 1794 })
            RecordLine(state, bytes, 0, cls, 100);
        Assert.Empty(state.OwnWallLocationLines);
    }

    [Fact]
    public void SeparateGRepFramesDoNotInvalidateTheSemanticRootAndExistingCandidatesStayUnchanged()
    {
        byte[] bytes = Block(pathValues: [0, 4, 17, -29, 40, 1, 0, 0]);
        var state = new ParsingState(); state.CurveDriverCandidates[100] = [];
        RecordLine(state, bytes, 0, 3524, 100);
        var line = Assert.Single(state.OwnWallLocationLines).Value;
        RecordLine(state, bytes, 0, 2007, 100);
        Assert.Same(line, state.OwnWallLocationLines[100]);
        state = new ParsingState(); var legacy = new CurveDriverData { EndParams = [0, 5], DirectionVector = [1, 0, 0] };
        state.CurveDriverCandidates[100] = [legacy]; RecordLine(state, bytes, 0, 3524, 100);
        Assert.Empty(state.OwnWallLocationLines);
        var model = new RevitFileInfo(); ElementBuilder.BuildElemTableElement(100, 0, state, model);
        Assert.Equal(5, Assert.Single(model.Elements).Location!.Length);
    }

    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void ResolvesOwnFacesThenPathAndPreservesPersistedGeneratorColumns(int cls)
    {
        var value = Read(Block(root: (ushort)cls))!;
        Assert.Equal(100, value.OwnerId); Assert.Equal(3, value.MaxId);
        var callback = Assert.Single(value.Callbacks);
        Assert.Equal(new ushort?[] { 1589, 1589, 1736 }, callback.Nodes.Select(n => n?.ClassId));
        Assert.Equal(new[] { 0, 1, 2 }, callback.Nodes.Select(n => n!.Tag));
        Assert.Equal(new[] { 11, -2, int.MinValue, int.MaxValue }, value.Slots.Select(s => s.Persisted.GeneratorId));
        Assert.Null(value.Slots[3].Node);
        Assert.All(callback.Nodes, n => Assert.True(n!.Start < n.End));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(5)]
    public void FacesWinOverLaterCurvesAtTheSameTagInEveryPhase(int phase)
    {
        var value = Read(Block(pathTag: 1, steps: [(phase, (ushort)4153, 0)]))!;
        Assert.Equal(1589, value.Slots[1].Node!.ClassId);
        Assert.Equal(phase, value.Callbacks[0].PhaseIndex);
    }

    [Fact]
    public void LaterFaceReplacesEarlierFaceWithoutMergingEqualHandles()
    {
        var value = Read(Block(faceTags: [1, 1], pathTag: -1))!;
        var callback = value.Callbacks.Single();
        Assert.NotEqual(callback.Nodes[0]!.QueueIndex, callback.Nodes[1]!.QueueIndex);
        Assert.Equal(callback.Nodes[1], value.Slots[1].Node);
    }

    [Fact]
    public void LaterAxisCurveReplacesEarlierPathCurve()
    {
        var value = Read(Block(faceTags: [], pathTag: 2, axisTags: [2],
            steps: [(0, (ushort)4153, 0), (5, (ushort)365, 0)]))!;
        Assert.Equal(value.Callbacks[1].Nodes[0], value.Slots[2].Node);
        Assert.NotEqual(value.Callbacks[0].Nodes.Last()!.QueueIndex, value.Slots[2].Node!.QueueIndex);
    }

    [Theory]
    [InlineData(0x100000)] [InlineData(0x100001)] [InlineData(-1)]
    public void SkipFlagDoesNotInvokeUnresolvedDriverBranchOrReusePreviousOutput(int flags)
    {
        var value = Read(Block(controlData: true,
            steps: [(0, (ushort)4153, flags), (1, (ushort)596, 0)]))!;
        Assert.True(value.Callbacks[0].Skipped); Assert.Empty(value.Callbacks[0].Nodes);
        Assert.False(value.Callbacks[1].Skipped); Assert.Empty(value.Callbacks[1].Nodes);
        Assert.All(value.Slots, s => Assert.Null(s.Node));
    }

    [Fact]
    public void EmptyBaseCallbackStartsFreshAfterReferenceCallback()
    {
        var value = Read(Block(steps: [(0, (ushort)4153, 0), (1, (ushort)596, 0)]))!;
        Assert.Equal(3, value.Callbacks[0].Nodes.Count); Assert.Empty(value.Callbacks[1].Nodes);
        Assert.Equal(3, value.Slots.Count(s => s.Node != null));
    }

    [Fact]
    public void FirstExactAxisCellIsUsedEvenWhenItsAxisIsNullAndAnotherCellExists()
    {
        var value = Read(Block(axisTags: [null, 2], steps: [(0, (ushort)365, 0)]))!;
        Assert.Null(Assert.Single(value.Callbacks[0].Nodes));
        Assert.All(value.Slots, s => Assert.Null(s.Node));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingAxisCellOrNullCellListProducesNoNodes(bool nullCells)
    {
        var value = Read(Block(nullCellList: nullCells, axisTags: [], steps: [(0, (ushort)365, 0)]))!;
        Assert.Empty(value.Callbacks[0].Nodes);
    }

    [Theory]
    [InlineData(-1)] [InlineData(4)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void NegativeOrBeyondNativeMaxTagsAreRetainedButDoNotWrite(int tag)
    {
        var value = Read(Block(faceTags: [], pathTag: tag))!;
        Assert.Equal(tag, Assert.Single(value.Callbacks[0].Nodes)!.Tag);
        Assert.All(value.Slots, s => Assert.Null(s.Node));
    }

    [Fact]
    public void NullPathIsRetainedAsANullCallbackSlot()
    {
        var value = Read(Block(nullPath: true))!;
        Assert.Null(value.Callbacks[0].Nodes.Last()); Assert.Null(value.Slots[2].Node);
    }

    [Theory]
    [InlineData(1736)] [InlineData(1974)]
    public void BothSourceBoundCurveKindsKeepTheirExactOwnObjectSpans(int cls)
    {
        var value = Read(Block(faceTags: [], pathClass: (ushort)cls))!;
        var node = Assert.Single(value.Callbacks[0].Nodes)!;
        Assert.Equal(cls, node.ClassId); Assert.Equal(cls == 1736 ? 80 : 113, node.End - node.Start);
        Assert.Equal(node, value.Slots[2].Node);
    }

    [Theory]
    [InlineData(0u)] [InlineData(0x80000u)] [InlineData(uint.MaxValue)]
    public void NodeFlagsAreRetainedNotUsedAsAnActiveFilter(uint flags)
    {
        var value = Read(Block(nodeFlags: flags))!;
        Assert.All(value.Callbacks[0].Nodes, n => Assert.Equal(flags, n!.Flags));
        Assert.Equal(3, value.Slots.Count(s => s.Node != null));
    }

    [Theory]
    [InlineData("owner")] [InlineData("control")] [InlineData("external-path")]
    [InlineData("footer")]
    public void UnresolvedContextAndInvalidFramesAreNotPromotedToACompletedPrefix(string mutation)
    {
        byte[] bytes = Block(ownerReference: mutation == "owner" ? 1u : 2u,
            controlData: mutation == "control", externalPath: mutation == "external-path");
        if (mutation == "footer") bytes[^1] ^= 1;
        Assert.Null(TfBlockDeserializer.ExtractOwnWallNonBRepTablePrefix(bytes, 0,
            BitConverter.ToUInt32(bytes, 8), 3524, out var failure));
        Assert.NotNull(failure);
    }

    private static TfBlockDeserializer.OwnWallNonBRepTablePrefix? Read(byte[] bytes)
    {
        var value = TfBlockDeserializer.ExtractOwnWallNonBRepTablePrefix(bytes, 0,
            BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt16(bytes, 12), out var failure);
        Assert.Null(failure); return Assert.IsType<TfBlockDeserializer.OwnWallNonBRepTablePrefix>(value);
    }

    private static byte[] Block(ushort root = 3524, int[]? faceTags = null, int pathTag = 2,
        int?[]? axisTags = null, (int Phase, ushort Class, int Flags)[]? steps = null,
        bool nullCellList = false, bool nullPath = false, bool controlData = false,
        bool externalPath = false, uint ownerReference = 2, uint nodeFlags = 4, ushort pathClass = 1736,
        double[]? pathValues = null)
    {
        faceTags ??= [0, 1]; axisTags ??= []; steps ??= [(0, 4153, 0)];
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        var queue = new Queue<Action>();
        w.Write(100); w.Write(0u); w.Write(0u); w.Write(root);
        foreach (var field in TfBlockDeserializer.SerializationFields(root)!)
            if (field.Declaring == 37 && field.Name == "m_id") w.Write(100);
            else if (field.Declaring == 37 && field.Name == "m_docAccess") w.Write(1u);
            else if (field.Name == "m_geomSteps") Pointer(2086, () =>
            {
                for (int phase = 0; phase < 6; phase++)
                {
                    var members = steps.Where(s => s.Phase == phase).ToArray(); w.Write((uint)members.Length);
                    foreach (var step in members) Pointer(step.Class, () =>
                    {
                        w.Write(new byte[16]); w.Write(77); w.Write(0); w.Write(step.Flags); w.Write(ownerReference);
                    });
                }
                w.Write(new byte[16]); w.Write(ownerReference); w.Write(new byte[20]); w.Write(0); w.Write((byte)0);
            });
            else if (field.Name == "m_pGeomTable") Pointer(2087, () =>
            {
                w.Write(-1); w.Write(0); w.Write((byte)0); w.Write(4u);
                foreach (int id in new[] { 11, -2, int.MinValue, int.MaxValue }) w.Write(id);
                w.Write(0u); w.Write(0u); w.Write(0u);
            });
            else if (field.Name == "m_cellList" && !nullCellList) Pointer(653, () =>
            {
                w.Write((uint)axisTags.Length);
                foreach (int? tag in axisTags) Pointer(953, () =>
                { if (tag == null) w.Write(0u); else Pointer(1736, () => Curve(tag.Value)); });
            });
            else if (field.Name == "m_pCurveDriver") Pointer(4040, () =>
            {
                foreach (var f in TfBlockDeserializer.SerializationFields(4040)!)
                    if (f.Name == "m_pCrv")
                    {
                        if (nullPath) w.Write(0u);
                        else if (externalPath) { w.Write(7u); w.Write((ushort)0); }
                        else Pointer(pathClass, () =>
                        {
                            Info(pathTag);
                            if (pathValues != null) { Assert.Equal(8, pathValues.Length); foreach (double v in pathValues) w.Write(v); }
                            else w.Write(new byte[pathClass == 1736 ? 64 : 97]);
                        });
                    }
                    else if (f.Name == "m_oControlData" && controlData) { w.Write(99u); w.Write((ushort)0); }
                    else Default(f);
            });
            else if (field.Name == "m_pRefFaces")
            {
                w.Write((uint)faceTags.Length);
                foreach (int tag in faceTags) Pointer(1589, () =>
                {
                    Info(tag); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);
                    w.Write(0); w.Write(0u); w.Write(0); w.Write(0u);
                }, 7u);
            }
            else Default(field);
        while (queue.TryDequeue(out var write)) write();
        w.Write(0u); var bytes = stream.ToArray(); uint size = (uint)bytes.Length - 16;
        BitConverter.GetBytes(size).CopyTo(bytes, 8); BitConverter.GetBytes(size).CopyTo(bytes, bytes.Length - 4);
        return bytes;

        void Pointer(ushort cls, Action body, uint handle = uint.MaxValue)
        { w.Write(handle); w.Write(cls); queue.Enqueue(body); }
        void Info(int tag) { w.Write(tag); w.Write(0); w.Write(-1); w.Write(nodeFlags); }
        void Curve(int tag) { Info(tag); w.Write(new byte[64]); }
        void Default(TfBlockDeserializer.TfField f)
        {
            if (f.Mods >> 4 == 5) { w.Write(0u); return; }
            int count = f.Mods >> 4 == 1 ? f.Cnt!.Value : 1;
            for (int i = 0; i < count; i++) switch (f.Type)
            {
                case 1: case 2: case 3: w.Write((byte)0); break;
                case 4: case 5: case 6: w.Write(0); break;
                case 7: case 10: w.Write(0L); break;
                case 8: w.Write(0u); break;
                case 9: w.Write(new byte[16]); break;
                case 13: w.Write(new byte[(f.Cnt ?? 1) * 8]); break;
                case 14 when (f.Mods & 15) == 0 && f.Ref is not null and not 20:
                    foreach (var nested in TfBlockDeserializer.SerializationFields(f.Ref.Value)!) Default(nested); break;
                case 14: w.Write(f.Ref == 20 ? -1 : 0); break;
                default: throw new InvalidOperationException(f.Name);
            }
        }
    }
}
