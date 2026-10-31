using RvtIO.Internal;
using RvtIO.Model;

namespace RvtIO.Parsing;

internal static partial class ElementBuilder
{
    internal static void ApplyOwnFamilyCutoutInputs(IReadOnlyList<Element> elements, ParsingState state)
    {
        var counts = elements.GroupBy(element => element.Id).ToDictionary(group => group.Key, group => group.Count());
        foreach (var element in elements)
        {
            element.OwnSingleFamilyCutout = null;
            element.OwnWallCutoutInstanceIds = null;
            element.OwnWallCutoutSteps = null;
            element.OwnWallAxisLine = null;
            element.OwnWallTrimmedAxis = null;
            element.OwnWallCrossSection = null;
            element.OwnWallHelper = null;
            if (element.Id <= 0 || counts[element.Id] != 1
                || !state.ComputedSymbolTargetClasses.TryGetValue((uint)element.Id, out var root)
                || root.BlockCount != 1) continue;
            if (root.ClassId is 3524 or 361
                && state.OwnWallHelpers.TryGetValue((uint)element.Id, out var helper) && helper.BlockCount == 1)
                element.OwnWallHelper = helper.Input;
            if (root.ClassId is 3524 or 361
                && state.OwnWallCutoutSteps.TryGetValue((uint)element.Id, out var wall)
                && wall is { BlockCount: 1, Steps: not null })
            {
                element.OwnWallCutoutInstanceIds = wall.Steps.Select(step => step.InstanceId).ToArray();
                element.OwnWallCutoutSteps = wall.Steps;
                element.OwnWallCrossSection = wall.CrossSection;
                if (wall.Axis is { OriginPoint.Length: 3, DirectionVector.Length: 3, EndParams.Length: 2 } axis)
                    element.OwnWallAxisLine = axis.EndParams.Select(parameter => Enumerable.Range(0, 3)
                        .Select(index => axis.OriginPoint[index] + parameter * axis.DirectionVector[index]).ToArray()).ToArray();
                if (wall.Axis is { } trimmedAxis && state.OwnWallTrimmedRanges.TryGetValue((uint)element.Id, out var range))
                    element.OwnWallTrimmedAxis = new[] { range.Lo, range.Hi }.Select(parameter => Enumerable.Range(0, 3)
                        .Select(index => trimmedAxis.OriginPoint[index] + parameter * trimmedAxis.DirectionVector[index]).ToArray()).ToArray();
            }
            if (root.ClassId != 1794 || element.ValidatedOriginalFamilySymbolId is not int symbolId
                || !state.ComputedFamilySymbols.TryGetValue((uint)element.Id, out var instance)
                || instance is not { BlockCount: 1, Symbol: { SubInstanceCount: 0, Transform.Length: 12,
                    HostId: > 0, InsertOrientation: 0 } source }
                || source.HostId != element.SerializedHostId
                || source.SymbolId != symbolId || !IsUniqueRoot(symbolId, 1829)
                || !counts.TryGetValue(symbolId, out int symbolCount) || symbolCount != 1
                || !state.OwnFamilyCutSymbols.TryGetValue((uint)symbolId, out var computed)
                || computed is not { BlockCount: 1, Symbol: { Active: true }, Loops.Count: 1 }
                || !state.FamilyInstanceSymbolIds.TryGetValue((uint)element.Id, out var type)
                || type is not { BlockCount: 1, TypeId: > 0 } || !IsUniqueRoot(type.TypeId.Value, 1829)
                || !state.OwnFamilyCutSymbols.TryGetValue((uint)type.TypeId.Value, out var master)
                || master is not { BlockCount: 1, Symbol: not null }
                || !IsUniqueRoot(master.Symbol.FamilyId, 1772)
                || !state.OwnFamilyInPlace.TryGetValue((uint)master.Symbol.FamilyId, out var family)
                || family is not { BlockCount: 1, InPlace: false }) continue;
            element.OwnSingleFamilyCutout = (symbolId, source.HostId.Value, source.Transform, computed.Loops[0]);
        }

        bool IsUniqueRoot(int id, ushort cls) => id > 0
            && state.ComputedSymbolTargetClasses.TryGetValue((uint)id, out var root)
            && root.BlockCount == 1 && root.ClassId == cls;
    }
}
