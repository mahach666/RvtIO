using RvtIO.Model;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>The Body of a plain IfcWall as the current profile writes it: the Brep of the decoded closed solid, the cuts of the wall (the
    /// holes and reveals of its doors and windows) included. A curved face belongs to the body only through the face mesh of its
    /// Geometry node (<see cref="WriteTriangleNodes"/>), never through its dense patches. False, with nothing written, when
    /// the geometry holds neither a closed solid nor a meshed Geometry node, a curved face has no mesh or the shell does not come out as a closed Brep.</summary>
    internal bool TryWriteDecodedPlainWallBodyCurrent(StepWriter w, Element elem, int subCtxId, out int shapeId)
    {
        shapeId = 0;
        if (!CurrentProfile || elem.Geometry is not { BrepSolids: { Count: > 0 } solids } geometry
            || !solids.Any(solid => solid.IsClosed) && geometry.MeshNodes is not { Count: > 0 })
            return false;
        var meshedNodes = geometry.MeshNodes?.Select(node => node.Node).ToHashSet() ?? [];
        if (geometry.Faces.Any(face => face.CurvedSurface && !meshedNodes.Contains(face.GeometryNode)))
            return false;
        var checkpoint = w.CreateCheckpoint();
        var decoded = CreateFacetedBrepWithOutcome(w, geometry, subCtxId, preserveValidatedPlanarFaces: true);
        if (decoded.ShapeId != 0 && decoded.Outcome is { HasSurfaceModel: false, HasClosedBrepOrSweptSolidBody: true })
        {
            shapeId = decoded.ShapeId;
            return true;
        }
        DiscardBodyItems(w, checkpoint);
        return false;
    }

    /// <summary>Takes back what was written since the checkpoint, the origins of its body items included (their ids are written again).</summary>
    private void DiscardBodyItems(StepWriter w, StepWriter.Checkpoint checkpoint)
    {
        foreach (int id in _bodyItemOrigins.Keys.Where(id => id >= checkpoint.NextId).ToList()) _bodyItemOrigins.Remove(id);
        w.Rollback(checkpoint);
    }

    /// <summary>The Body of a room whose boundary holds round walls: one IfcFacetedBrep per Geometry node with the triangles of the face
    /// meshes (<see cref="WriteTriangleNodes"/>). False, with nothing written, unless every node comes out as a triangle set.</summary>
    private bool TryWriteMeshedSpaceBodyCurrent(StepWriter w, GeometryData boundary, int subCtxId, double offZ, out FacetedShapeWriteResult result)
    {
        result = default;
        if (!CurrentProfile || boundary.MeshNodes is not { Count: > 0 } nodes) return false;
        var checkpoint = w.CreateCheckpoint();
        var items = new List<(long Order, int Node, int BrepId)>();
        var written = WriteTriangleNodes(w, boundary, 0, 0, offZ, null, items);
        if (written.Count != nodes.Count || boundary.Faces.Any(face => !written.Contains(face.GeometryNode)))
        {
            DiscardBodyItems(w, checkpoint);
            return false;
        }
        int representationId = w.Add("IFCSHAPEREPRESENTATION",
            $"{R(subCtxId)},{S("Body")},{S("Brep")},{StepWriter.List(items.OrderBy(item => item.Order).ThenBy(item => item.Node).Select(item => R(item.BrepId)))}");
        result = new FacetedShapeWriteResult([representationId],
            new BodyRepresentationWriteOutcome(HasSurfaceModel: false, HasClosedBrepOrSweptSolidBody: true, OpenShellCount: 0, OpenShellFaceCount: 0));
        return true;
    }
}
