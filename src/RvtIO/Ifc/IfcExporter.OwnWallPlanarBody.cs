using System.Globalization;
using RvtIO.Model;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    internal int ApplyOwnWallPlanarBodies(StepWriter w, IReadOnlyDictionary<int, Element> elements,
        IReadOnlyDictionary<int, int> occurrences, IReadOnlyDictionary<int, int> productElementIds,
        int subContext, int buildingPlacement, int identityAxis)
    {
        var voidHosts = new HashSet<int>();
        int count = w.EntityCount;
        for (int i = 0; i < count; i++)
        {
            var entity = w.EntityAt(i);
            if (entity.Type != "IFCRELVOIDSELEMENT") continue;
            var args = SplitStepArguments(entity.Args);
            if (args.Length != 6 || args[4].Length < 2 || args[4][0] != '#'
                || !int.TryParse(args[4].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int host))
                throw new InvalidOperationException("Invalid written IfcRelVoidsElement host.");
            voidHosts.Add(host);
        }
        int replaced = 0;
        var replacedShapes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            var entity = w.EntityAt(i);
            // shortcut: void-bearing hosts and StandardCase keep their existing contract;
            // extend only after the source Body/Axis/opening operands are shown to agree.
            if (entity.Type != "IFCWALL" || voidHosts.Contains(entity.Id)
                || !productElementIds.TryGetValue(entity.Id, out int id)
                || occurrences.GetValueOrDefault(id) != 1
                || !elements.TryGetValue(id, out var element)
                || element.OwnPrimaryPlanarBody is not { } body
                // Planarity looks at every face of a node, the inactive ones included: a node with a round face is the triangles of its
                // face meshes, not the polygons of its planar faces
                || CurrentProfile && element.Geometry is { MeshNodes.Count: > 0 }) continue;
            var args = SplitStepArguments(entity.Args);
            if (args.Length != 8) throw new InvalidOperationException("Invalid written IfcWall attributes.");
            var checkpoint = w.CreateCheckpoint();
            var written = CreateFacetedShapeRepresentations(w, body, subContext,
                preserveValidatedPlanarFaces: true, preserveValidatedCoordinates: true);
            if (written.RepresentationIds.Count == 0 || written.Outcome.HasSurfaceModel
                || !written.Outcome.HasClosedBrepOrSweptSolidBody)
            { w.Rollback(checkpoint); continue; }
            int shape = w.Add("IFCPRODUCTDEFINITIONSHAPE", $"$,$,{StepWriter.List(written.RepresentationIds.Select(R))}");
            int placement = w.Add("IFCLOCALPLACEMENT", $"{R(buildingPlacement)},{R(identityAxis)}");
            // the body written before is no longer referred to: removed below with what only it referred to
            if (TryEntityId(args[6], out int previousShape)) replacedShapes.Add(previousShape);
            args[5] = R(placement); args[6] = R(shape);
            w.ReplaceArgs(i, string.Join(',', args));
            replaced++;
        }
        if (replacedShapes.Count > 0) RemoveDetachedCurrent(w, replacedShapes);
        return replaced;
    }
}
