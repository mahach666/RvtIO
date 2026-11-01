using System.Text;

namespace RvtIO.Parsing;

/// <summary>
/// Global/DocumentIncrementTable, decoded by the file's own schema: the class DocumentIncrementTable
/// (m_increments, m_localIncrements: DocumentIncrement rows; m_permutation: pairs), each field read by its schema type
/// (04/05 = 4 bytes, 0B = 8, 01 = 1, string storage = u32 character count + UTF-16, class = its fields, dynamic container
/// = u32 count + elements). One DocumentIncrement is one save increment; its ordinal is the number of its physical partition
/// stream (Partitions/N). Its m_greatest is the last modification episode the increment holds, and m_incrementVersions the
/// chronological (causing increment, element count still current in this increment) pairs.
///
/// An element row's m_lastModificationDate is the episode that selects the increment: the first increment whose greatest
/// episode is not below it. An increment that keeps no element redirects to the increment its terminal version names.
/// Every routing fact was measured against the partition streams of the real files.
/// Every shape violation yields null: nothing is guessed. The decoder is bounded by a node budget (the stream length), so a hostile
/// schema or count cannot make it allocate or run past the size of the data.
/// </summary>
internal static class DocumentIncrementTable
{
    internal sealed record Version(long CausedBy, long TotalElements);

    internal sealed record Increment(long Greatest, long TotalEpisodes, long TotalElements, IReadOnlyList<Version> Versions)
    {
        internal bool SameRouting(Increment other) => Greatest == other.Greatest && TotalEpisodes == other.TotalEpisodes
            && TotalElements == other.TotalElements && Versions.SequenceEqual(other.Versions);
    }

    internal sealed record Table(IReadOnlyList<Increment> Increments, IReadOnlyList<Increment> Local, IReadOnlyList<(long First, long Second)> Permutation);

    private sealed class Reader(byte[] data, FormatsSchema schema)
    {
        internal int Position;
        // Every decoded class instance costs one node. A real table spends at least one byte per node, so the stream length
        // bounds the work; a schema with empty classes or a huge count cannot make the decoder run or allocate past it.
        private long _nodes = data.Length;

        private void Need(long count)
        {
            if (count < 0 || Position + count > data.Length) throw new FormatException("increment table truncated");
        }

        internal object Class(FormatsSchema.ClassDef def, int depth)
        {
            if (depth > 8 || def.Base != 0) throw new FormatException($"unsupported class {def.Name}");
            if (--_nodes < 0) throw new FormatException("node budget exceeded");
            var values = new Dictionary<string, object>();
            foreach (var field in def.Fields) values[field.Name] = Field(field, depth);
            return values;
        }

        private object Field(FormatsSchema.Field field, int depth)
        {
            int storage = field.Mods >> 4;
            if (storage == 5)
            {
                if (field.Ref is not int r || !schema.Classes.TryGetValue(r, out var element)) throw new FormatException($"container {field.Name}");
                Need(4);
                uint count = BitConverter.ToUInt32(data, Position);
                Position += 4;
                if (count > _nodes) throw new FormatException("container count");
                var list = new List<object>();
                for (uint i = 0; i < count; i++) list.Add(Class(element, depth + 1));
                return list;
            }
            if (storage == 6 && field.Type == 8 && (field.Mods & 0x0F) == 0)
            {
                Need(4);
                uint chars = BitConverter.ToUInt32(data, Position);
                Position += 4;
                Need(2L * chars);
                string text = Encoding.Unicode.GetString(data, Position, (int)(2 * chars));
                Position += (int)(2 * chars);
                return text;
            }
            if (storage != 0) throw new FormatException($"field {field.Name} storage {storage}");
            switch (field.Type)
            {
                case 1: Need(1); return data[Position++] != 0;
                case 4: { Need(4); int v = BitConverter.ToInt32(data, Position); Position += 4; return (long)v; }
                case 5: { Need(4); uint v = BitConverter.ToUInt32(data, Position); Position += 4; return (long)v; }
                case 0x0B: { Need(8); long v = BitConverter.ToInt64(data, Position); Position += 8; return v; }
                case 0x0E when (field.Mods & 0x0F) == 0 && field.Ref is int r && schema.Classes.TryGetValue(r, out var inner):
                    return Class(inner, depth + 1);
                default: throw new FormatException($"field {field.Name} type {field.Type:X2}");
            }
        }
    }

    /// <summary>The table of a decoded Global/DocumentIncrementTable stream, or null unless the stream is exactly this
    /// schema's DocumentIncrementTable (class id first, every field consumed, only zero padding after) and its global and
    /// local rows have the same count. <paramref name="failure"/> names the violated expectation.</summary>
    internal static Table? TryParse(byte[] data, FormatsSchema schema, out string? failure)
    {
        failure = null;
        if (schema.Get("DocumentIncrementTable") is not { } def) { failure = "no DocumentIncrementTable class"; return null; }
        if (data.Length < 2 || BitConverter.ToUInt16(data, 0) != def.Id) { failure = "class id"; return null; }
        try
        {
            var reader = new Reader(data, schema) { Position = 2 };
            var root = (Dictionary<string, object>)reader.Class(def, 0);
            if (data.Skip(reader.Position).Any(b => b != 0)) { failure = "trailing bytes"; return null; }
            var increments = Rows(root["m_increments"]);
            var local = Rows(root["m_localIncrements"]);
            if (increments.Count != local.Count) { failure = "global/local row count"; return null; }
            var permutation = ((List<object>)root["m_permutation"]).Cast<Dictionary<string, object>>()
                .Select(p => ((long)((Dictionary<string, object>)p["first"])["m_id"], (long)p["second"]))
                .ToList();
            return new Table(increments, local, permutation);
        }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidCastException)
        {
            failure = exception.Message;
            return null;
        }
    }

    private static List<Increment> Rows(object value) =>
        ((List<object>)value).Cast<Dictionary<string, object>>().Select(row => new Increment(
            (long)((Dictionary<string, object>)row["m_greatest"])["m_id"], (long)row["m_totalEpisodes"], (long)row["m_totalElements"],
            ((List<object>)row["m_incrementVersions"]).Cast<Dictionary<string, object>>()
                .Select(v => new Version((long)v["m_causedByIncrementNumber"], (long)v["m_totalElements"])).ToList())).ToList();

    /// <summary>The increment (= partition stream ordinal) a modification episode routes to, or null when the episode lies
    /// outside every increment, the version history of the increment it lands on is not chronological, or an empty increment
    /// has no later increment to redirect to. Facts used (measured on real files): the
    /// increment is the first whose greatest episode reaches the episode; the last version of an increment states how many
    /// elements it still keeps; an increment that keeps none was compacted into the increment its last version names.</summary>
    internal static int? Route(Table table, long episode)
    {
        var rows = table.Increments;
        int index = -1;
        for (int i = 0; i < rows.Count && index < 0; i++)
            if (rows[i].Greatest >= episode) index = i;
        // a redirect always points to a later increment, so the walk ends after at most rows.Count steps
        while (index >= 0 && index < rows.Count)
        {
            var row = rows[index];
            var versions = row.Versions;
            bool chronological = versions.Zip(versions.Skip(1)).All(p => p.First.CausedBy < p.Second.CausedBy && p.First.TotalElements >= p.Second.TotalElements);
            if (!chronological || versions.Any(v => v.CausedBy < -1)) return null;
            var last = versions.Count > 0 ? versions[^1] : null;
            if (last != null && last.TotalElements != row.TotalElements) return null;
            if (row.TotalElements > 0) return index;
            if (last == null || last.CausedBy <= index) return null;
            index = (int)last.CausedBy;
        }
        return null;
    }
}
