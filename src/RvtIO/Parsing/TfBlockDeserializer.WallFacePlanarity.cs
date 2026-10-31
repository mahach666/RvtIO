namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record WallFaceSurfaceSource(string Archive, WallPhasePointer Pointer, int? Start, int? End);
    internal sealed record WallFacePlanarityVisit(int RowIndex, WallHistoryEntry History, WallTableNode Face,
        WallFaceSurfaceSource Surface, int SurfaceType, bool IsPlanar);
    internal sealed record WallFacePlanarityGenerator(int PhaseIndex, int StepQueueIndex, ushort ClassId,
        int GeneratorId, int Flags, IReadOnlyList<WallFacePlanarityVisit> Faces);
    internal sealed record OwnWallDebugOffFacePlanarity(int OwnerId, ushort ClassId,
        IReadOnlyList<WallFacePlanarityGenerator> Generators);

    /// <summary>Face.surfaceType and Face.isPlanar for the own Plane (522), CylSurf (983) and null surface paths.
    /// The factory-installed virtual 24 returns 1 or 2; null returns 0; isPlanar is type == 1, not a vector/epsilon test.
    /// Uses supplied conditional active Faces and qualified own Wall/GRep links. External, hand, other and
    /// modeler-type 7 surfaces are explicitly unresolved. Does not establish the runtime state, loops, cut/extrusion or IFC.</summary>
    internal static OwnWallDebugOffFacePlanarity? SelectOwnWallDebugOffFacePlanarity(OwnWallCompleteInputs input,
        OwnWallReferenceGeometryInputs references, OwnGElementGeometryGraph graph,
        OwnWallDebugOffActiveFaceHistories active, out string? failure)
    {
        var list = input.Histories.Histories.List;
        failure = "planarity-owner";
        if (!HasGElementGraphSchemas() || !input.Histories.Histories.Complete || list.OwnerReference != 2
            || list.OwnerId <= 0 || list.OwnerId != active.OwnerId || list.OwnerId != references.OwnerId || list.OwnerId != graph.OwnerId
            || list.ClassId is not (3524 or 361) || list.ClassId != active.ClassId || list.ClassId != references.ClassId
            || references.ListOwnerReference != 2 || active.MaxId < -1 || active.MaxId > 100_000) return null;
        var ownFaces = references.Faces.ToDictionary(n => n.QueueIndex);
        var ownLate = input.LaterObjects.ToDictionary(n => n.QueueIndex);
        var physical = graph.Nodes.ToDictionary(n => n.QueueIndex);
        var generators = new List<WallFacePlanarityGenerator>();
        string context = "planarity";
        try
        {
            foreach (var generator in active.Generators)
            {
                var faces = new List<WallFacePlanarityVisit>();
                foreach (var row in generator.Visits.Where(r => r.Outcome == "active"))
                {
                    context = $"planarity-face:{generator.StepQueueIndex}:{row.RowIndex}";
                    if (row.TableNode is not { ClassId: 1589 } face || (face.Flags & (1u << 19)) == 0
                        || face.Tag != row.History.Id || face.Tag < 0 || face.Tag > active.MaxId) throw Fail;
                    WallPhasePointer pointer;
                    if (face.Archive == "Wall")
                    {
                        if (!ownFaces.TryGetValue(face.QueueIndex, out var own) || own.Tag != face.Tag || own.Flags != face.Flags
                            || own.Start != face.Start || own.End != face.End) throw Fail;
                        pointer = own.Surface;
                    }
                    else if (face.Archive == "GRep")
                    {
                        if (!physical.TryGetValue(face.QueueIndex, out var own) || own.ClassId != 1589
                            || own.Start != face.Start || own.End != face.End) throw Fail;
                        var info = own.Fields.Single(f => f.Declaring == 1246 && f.Name == "m_GInfo").Items.Single().Values;
                        if (info.Count != 4 || info[0] != face.Tag || info[3] != face.Flags) throw Fail;
                        var p = own.Fields.Single(f => f.Declaring == 1589 && f.Name == "m_pSurf").Items.Single().Pointer!;
                        if (p.HandToken != null) throw Fail;
                        pointer = new(p.Handle, p.ClassId, p.QueueIndex, p.Start, p.End);
                    }
                    else throw Fail;
                    context = $"planarity-surface:{face.Archive}:{face.QueueIndex}:{pointer.QueueIndex}";
                    int? first = null, end = null; int type;
                    if (pointer.Handle == 0 && pointer.ClassId == null && pointer.QueueIndex == null) type = 0;
                    else
                    {
                        if (pointer.Handle == 0 || pointer.QueueIndex is not int index || pointer.ClassId is not (522 or 983)) throw Fail;
                        if (face.Archive == "Wall")
                        {
                            if (!ownLate.TryGetValue(index, out var surface) || surface.ClassId != pointer.ClassId || surface.Handle != pointer.Handle) throw Fail;
                            first = surface.Start; end = surface.End;
                        }
                        else
                        {
                            if (!physical.TryGetValue(index, out var surface) || surface.ClassId != pointer.ClassId || surface.Handle != pointer.Handle) throw Fail;
                            first = surface.Start; end = surface.End;
                        }
                        type = pointer.ClassId == 522 ? 1 : 2; // exact source-bound virtual24 leaves
                    }
                    faces.Add(new(row.RowIndex, row.History, face, new(face.Archive, pointer, first, end), type, type == 1));
                }
                generators.Add(new(generator.PhaseIndex, generator.StepQueueIndex, generator.ClassId,
                    generator.GeneratorId, generator.Flags, faces));
            }
            failure = null; return new(list.OwnerId, list.ClassId, generators);
        }
        catch (DecodeFail) { failure = context; return null; }
    }
}
