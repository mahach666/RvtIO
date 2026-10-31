using RvtIO.Internal;
using RvtIO.Model;

namespace RvtIO.Parsing;

internal static partial class ElementBuilder
{
    internal static void ApplyValidatedProfileRoofAreas(IReadOnlyList<Element> elements, ParsingState state)
    {
        foreach (var element in elements) element.ValidatedProfileRoofAreaFeet = null;
        foreach (var group in elements.Where(element => state.OwnProfileRoofAreas.ContainsKey((uint)element.Id))
                     .GroupBy(element => element.Id))
        {
            if (group.Count() != 1) continue;
            var element = group.Single();
            double area = state.OwnProfileRoofAreas[(uint)element.Id];
            if (!element.IsBlockOnly && element.CategoryId == -2000035 && double.IsFinite(area) && area > 0
                && state.OwnedElementClassRoots.TryGetValue((uint)element.Id, out var root)
                && root.BlockCount == 1 && root.ClassId == 3054)
                element.ValidatedProfileRoofAreaFeet = area;
        }
    }
}
