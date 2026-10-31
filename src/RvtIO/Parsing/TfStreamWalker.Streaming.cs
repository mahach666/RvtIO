namespace RvtIO.Parsing;

internal static partial class TfStreamWalker
{
    /// <summary>Diagnostic streaming adapter using Walk's exact segment/overlap and
    /// private anchor parser. Partition boundaries do not reset the continuous stream.
    /// The existing main-reader Walk implementation is intentionally unchanged.</summary>
    internal static void WalkStreaming(IEnumerable<byte[]> chunks, HashSet<uint> knownIds,
        BlockVisitor visit, int segmentTarget = 1_400_000_000)
    {
        // A resynchronization anchor requires its next item too. The smaller test
        // segment limit must fit carried overlap and both maximum-sized items.
        if (segmentTarget <= 3 * MaxBlock) throw new ArgumentOutOfRangeException(nameof(segmentTarget));
        using var input = chunks.GetEnumerator();
        bool hasChunk = input.MoveNext();
        var carry = Array.Empty<byte>();
        while (hasChunk)
        {
            long size = carry.Length;
            var batch = new List<byte[]>();
            while (hasChunk && size + input.Current.Length <= segmentTarget)
            {
                batch.Add(input.Current); size += input.Current.Length;
                hasChunk = input.MoveNext();
            }
            if (batch.Count == 0)
            {
                batch.Add(input.Current); size += input.Current.Length;
                hasChunk = input.MoveNext();
            }
            var segment = new byte[size];
            int offset = carry.Length;
            Buffer.BlockCopy(carry, 0, segment, 0, carry.Length);
            foreach (var chunk in batch)
            {
                Buffer.BlockCopy(chunk, 0, segment, offset, chunk.Length);
                offset += chunk.Length;
            }
            batch.Clear();
            bool last = !hasChunk;
            WalkSegment(segment, knownIds, visit, last ? segment.Length : Math.Max(0, segment.Length - MaxBlock));
            if (!last)
            {
                int tail = Math.Min(MaxBlock, segment.Length);
                carry = new byte[tail];
                Buffer.BlockCopy(segment, segment.Length - tail, carry, 0, tail);
            }
        }
    }
}
