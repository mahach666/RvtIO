using RvtIO.Internal;
using RvtIO.Model;

namespace RvtIO.Parsing;

internal static partial class ElementBuilder
{
    internal static void ApplyValidatedRailingLengths(IReadOnlyList<Element> elements, ParsingState state)
    {
        foreach (var element in elements) element.ValidatedRailingLengthFeet = null;
        foreach (var group in elements.Where(e => e.CategoryId == -2000126 && !e.IsBlockOnly).GroupBy(e => e.Id))
        {
            if (group.Count() != 1) continue;
            var element = group.Single();
            if (!state.OwnedElementClassRoots.TryGetValue((uint)element.Id, out var root)
                || root.BlockCount != 1 || root.ClassId != 485
                || !state.RailingQuantityInputs.TryGetValue((uint)element.Id, out var occurrence)
                || occurrence.BlockCount != 1 || occurrence.Input is not { ClassId: 485, SymbolId: > 0 } own
                || own.OwnerId != element.Id || own.SymbolId == element.Id
                || !state.OwnedElementClassRoots.TryGetValue((uint)own.SymbolId.Value, out var symbolRoot)
                || symbolRoot.BlockCount != 1 || symbolRoot.ClassId != 492
                || !state.RailingQuantityInputs.TryGetValue((uint)own.SymbolId.Value, out var symbol)
                || symbol.BlockCount != 1 || symbol.Input is not { ClassId: 492, LengthFeet: > 0 } source
                || source.OwnerId != own.SymbolId || source.RailingId != element.Id
                || !double.IsFinite(source.LengthFeet.Value)) continue;
            // Native CURVE_ELEM_LENGTH reads this stored value; a negative value needs the separate
            // path calculator.
            element.ValidatedRailingLengthFeet = source.LengthFeet.Value;
        }
    }
}
