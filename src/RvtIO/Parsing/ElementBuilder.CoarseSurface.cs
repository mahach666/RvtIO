using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;

namespace RvtIO.Parsing;

internal static partial class ElementBuilder
{
    private const int DoorsCategoryId = -2000023, WindowsCategoryId = -2000014;

    /// <summary>
    /// The Coarse solids are placed with the same rules as the Fine ones (<see
    /// cref="ExpandInstanceGeometry"/>); the area is kept for the elements whose blocks have a
    /// differing Coarse level, the others have the same solids at both levels.
    /// </summary>
    internal static void ApplyCoarseDoorWindowSurfaces(RevitFileInfo info, ParsingState state)
    {
        foreach (var element in info.Elements)
        {
            if (element.CategoryId is not (DoorsCategoryId or WindowsCategoryId)) continue;
            uint id = (uint)element.Id;
            state.GrepBlocks.TryGetValue(id, out var grep);
            state.FamilyInstanceTransforms.TryGetValue(id, out var transformOnly);
            var visited = new HashSet<uint>();
            if (!HasCoarseAlternative(grep, state, visited)
                && !(transformOnly != null && state.GrepBlocks.TryGetValue((uint)Math.Max(transformOnly.SymbolId, 0), out var symbol)
                    && HasCoarseAlternative(symbol, state, visited)))
                continue;

            state.CoarseView = true;
            try
            {
                var coarse = new Element { Id = element.Id, TypeId = element.TypeId, BoundingBox = element.BoundingBox };
                var own = grep?.Coarse ?? grep;
                if (own != null)
                {
                    coarse.Geometry = BuildGeometryData(own);
                    ExpandInstanceGeometry(coarse, own, element.TypeId, state);
                }
                ExpandTransformOnlyInstanceGeometry(coarse, id, element.TypeId, state);
                element.CoarseSurfaceArea = IfcExporter.SurfaceAreaCurrent(coarse.Geometry);
            }
            finally
            {
                state.CoarseView = false;
            }
        }
    }

    /// <summary>
    /// True when the block, or a symbol it places (at any depth), holds a Coarse level that differs
    /// from its Fine one.
    /// </summary>
    private static bool HasCoarseAlternative(GRepData? block, ParsingState state, HashSet<uint> visited)
    {
        if (block == null) return false;
        if (block.Coarse != null) return true;
        foreach (var instance in block.Instances)
            if (instance.SymbolElemId > 0 && visited.Add((uint)instance.SymbolElemId)
                && state.GrepBlocks.TryGetValue((uint)instance.SymbolElemId, out var symbol)
                && HasCoarseAlternative(symbol, state, visited))
                return true;
        return false;
    }
}
