using OpenMcdf;
using RvtIO.Parsing;

namespace RvtIO;

public partial class RvtFileReader
{
    /// <summary>The partition chunks of a file with the class schema of a later release (<paramref name="schema"/>) rewritten into the
    /// Revit 2022 layout (see <see cref="R22Transcoder"/>); the chunks after the partition streams are kept. Null when the file's
    /// schema table does not parse a second time or its id width is not 4 or 8 bytes.</summary>
    private static List<byte[]>? RewriteToR22Layout(RootStorage root, FileSchema schema, List<byte[]> chunks,
        IReadOnlyList<IReadOnlyList<byte[]>> partitionStreams, TextWriter? diag)
    {
        if (WideElementRecords.IdWidth(schema) is not int width
            || PagedGzipStream.Read(root, "Formats", "Latest") is not { } schemaBytes)
            return null;
        SchemaDocument source;
        try { source = TfSchemaReader.Parse(schemaBytes); }
        catch (FormatException) { return null; }
        using var shipped = typeof(RvtFileReader).Assembly.GetManifestResourceStream("RvtIO.Resources.r22_schemas.json");
        if (shipped == null) return null;
        var transcoder = new R22Transcoder(source, TfSchemaReader.ReadShipped2022(shipped), width);
        var rewritten = new List<byte[]>(chunks.Count);
        foreach (var stream in partitionStreams)
        {
            rewritten.AddRange(transcoder.Stream(stream, out var stats));
            diag?.WriteLine($"Revit 2022 layout: {stats.Records} records ({stats.RecordsRewritten} rewritten), "
                + $"{stats.RootBlocks} root blocks ({stats.RootBlocksRewritten}), {stats.OtherBlocks} other blocks ({stats.OtherBlocksRewritten}), "
                + $"{stats.UnparsedBytes} bytes outside the section grammar");
            if (stats.StoppedBytes != null)
                diag?.WriteLine($"  section grammar stopped at offset {stats.StoppedAt}: {stats.StoppedBytes}");
            foreach (var (reason, count) in stats.Failures.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal).Take(12))
                diag?.WriteLine($"  not rewritten: {count} x {reason}");
        }
        rewritten.AddRange(chunks.Skip(partitionStreams.Sum(s => s.Count)));
        return rewritten;
    }
}
