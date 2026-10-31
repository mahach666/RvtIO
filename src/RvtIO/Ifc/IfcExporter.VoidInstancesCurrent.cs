using RvtIO.Model;

namespace RvtIO.Ifc;

public partial class IfcExporter
{
    /// <summary>Bit 0x200 of the GInfo flags of a serialized Face. Every element has at least one
    /// Face with it, except those whose Faces are all display (0x00200000) or auxiliary (0x04000000) surfaces, which never
    /// reach the body.</summary>
    internal const uint FaceFillFlag = 0x00000200;

    /// <summary>Whether every decoded Face of the element lacks <see cref="FaceFillFlag"/>. The current profile writes no such instance:
    /// an instance whose own geometry is null is not exported (for example the floor-hosted family
    /// "Assignment - Rectangular opening (floor)"). Faces whose flags are not known (placed from a nested symbol) count as filled.</summary>
    internal static bool HasOnlyUnfilledFacesCurrent(Element element) =>
        element.Geometry?.Faces is { Count: > 0 } faces
        && faces.All(face => face.GInfoFlags is uint flags && (flags & FaceFillFlag) == 0);
}
