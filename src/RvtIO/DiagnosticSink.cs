namespace RvtIO;

/// <summary>
/// The destination of the diagnostic messages of the parsers and exporters. Nothing is written unless a
/// public entry point (a reader, an exporter) was given a <c>DiagnosticOutput</c>; the writer is scoped to the
/// logical call, so concurrent reads with different writers do not mix.
/// </summary>
internal static class DiagnosticSink
{
    private static readonly AsyncLocal<TextWriter?> Current = new();

    /// <summary>Makes <paramref name="writer"/> the sink until the returned scope is disposed.
    /// A null writer keeps the sink of the enclosing scope.</summary>
    internal static IDisposable Use(TextWriter? writer) => writer == null ? NullScope.Instance : new Scope(writer);

    /// <summary>Writes one line to the current sink; does nothing when there is none.</summary>
    internal static void WriteLine(string message)
    {
        var writer = Current.Value;
        if (writer == null) return;
        lock (writer) writer.WriteLine(message);
    }

    private sealed class Scope : IDisposable
    {
        private readonly TextWriter? _previous = Current.Value;
        internal Scope(TextWriter writer) => Current.Value = writer;
        public void Dispose() => Current.Value = _previous;
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
