using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class DiagnosticOutputTests
{
    [Fact]
    public void Sink_WritesToTheScopedWriter_AndNowhereWithoutOne()
    {
        DiagnosticSink.WriteLine("no scope");
        var writer = new StringWriter();
        using (DiagnosticSink.Use(writer))
        {
            DiagnosticSink.WriteLine("inside");
        }
        DiagnosticSink.WriteLine("after the scope");
        Assert.Equal("inside" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void Sink_NullWriterKeepsTheEnclosingScope_AndInnerScopeRestoresIt()
    {
        var outer = new StringWriter();
        var inner = new StringWriter();
        using (DiagnosticSink.Use(outer))
        {
            using (DiagnosticSink.Use(null)) DiagnosticSink.WriteLine("kept");
            using (DiagnosticSink.Use(inner)) DiagnosticSink.WriteLine("inner");
            DiagnosticSink.WriteLine("restored");
        }
        Assert.Equal("kept" + Environment.NewLine + "restored" + Environment.NewLine, outer.ToString());
        Assert.Equal("inner" + Environment.NewLine, inner.ToString());
    }

    [Fact]
    public async Task Sink_FollowsTheCallIntoParallelWork_WithoutMixingWriters()
    {
        var first = new StringWriter();
        var second = new StringWriter();
        async Task Run(StringWriter writer, string text)
        {
            using var scope = DiagnosticSink.Use(writer);
            await Task.Run(() => Parallel.For(0, 50, _ => DiagnosticSink.WriteLine(text)));
        }
        await Task.WhenAll(Run(first, "a"), Run(second, "b"));
        Assert.Equal(50, first.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Count(line => line == "a"));
        Assert.DoesNotContain("b", first.ToString());
        Assert.Equal(50, second.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Count(line => line == "b"));
        Assert.DoesNotContain("a", second.ToString());
    }

    [Fact]
    public void IfcExporter_ReportsToItsDiagnosticOutput_AndIsSilentByDefault()
    {
        var info = new RevitFileInfo();
        var diagnostics = new StringWriter();
        new IfcExporter { DiagnosticOutput = diagnostics }.Export(info, new StringWriter());
        Assert.Contains("IFC export:", diagnostics.ToString());

        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try { new IfcExporter().Export(info, new StringWriter()); }
        finally { Console.SetError(original); }
        Assert.Equal("", captured.ToString());
    }
}
