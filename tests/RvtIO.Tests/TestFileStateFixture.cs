using RvtIO;
using RvtIO.Internal;
using Xunit;

namespace RvtIO.Tests;

/// <summary>
/// Shared fixture: parses TESTFILE.rvt once via the internal <see cref="RvtFileReader.ReadWithState"/>
/// entry point, exposing <see cref="ParsingState"/> to tests that need production-path internals
/// (not just the public <c>RevitFileInfo</c> model). <see cref="ParsingState"/> is an internal type,
/// so this property must also be declared internal - a public property of an internal type is
/// CS0053 - while the fixture class itself stays public, as xUnit requires for <see cref="IClassFixture{TFixture}"/>.
/// </summary>
public class TestFileStateFixture : IDisposable
{
    internal ParsingState State { get; }

    public TestFileStateFixture()
    {
        string rvtPath = TestFileFixture.FindTestFile("TESTFILE.rvt");
        var reader = new RvtFileReader();
        var (_, state) = reader.ReadWithState(rvtPath);
        State = state;
    }

    public void Dispose() { }
}
