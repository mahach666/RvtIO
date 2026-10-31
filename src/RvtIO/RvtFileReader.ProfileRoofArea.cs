using RvtIO.Internal;
using RvtIO.Parsing;

namespace RvtIO;

public partial class RvtFileReader
{
    internal static void ReadOwnProfileRoofAreas(ParsingState state)
    {
        state.OwnProfileRoofAreas.Clear();
        var owners = state.OwnedElementClassRoots.Where(pair => pair.Value.BlockCount == 1
            && pair.Value.ClassId == 3054
            && state.SystemOccurrenceTypeIds.TryGetValue(pair.Key, out var link)
            && link.BlockCount == 1 && link.ClassId == 3054 && link.TypeId is > 0
            && state.SystemTypeRoots.TryGetValue((uint)link.TypeId.Value, out var type)
            && type.BlockCount == 1 && type.ClassId == 3452).Select(pair => pair.Key).ToHashSet();
        if (owners.Count == 0) return;
        var blocks = new Dictionary<uint, List<(byte[] Data, int Start, uint Size, ushort Class)>>();
        TfStreamWalker.Walk(state.Chunks, state.AllKnownIds, (data, start, size, cls, owner) =>
        {
            if (cls is not (3054 or 2007) || !owners.Contains(owner)) return;
            if (!blocks.TryGetValue(owner, out var list)) blocks.Add(owner, list = []);
            list.Add((data, start, size, cls));
        });
        foreach (var (owner, list) in blocks)
        {
            var roots = list.Where(block => block.Class == 3054).ToArray();
            var greps = list.Where(block => block.Class == 2007).ToArray();
            if (roots.Length != 1 || greps.Length != 1) continue;
            var root = roots[0]; var grep = greps[0];
            var tags = TfBlockDeserializer.ExtractOwnProfileRoofAreaFaceTags(root.Data, root.Start, root.Size);
            if (tags == null || tags.OwnerId != owner) continue;
            var graph = TfBlockDeserializer.ExtractOwnGElementGeometryGraph(grep.Data, grep.Start, grep.Size,
                grep.Class, out _, includeAnnotationNodes: true);
            if (graph != null && GRepParserV2.ComputeOwnProfileRoofArea(graph, tags) is double area)
                state.OwnProfileRoofAreas[owner] = area;
        }
    }
}
