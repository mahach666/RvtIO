using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace RvtIO.Tests;

/// <summary>A missing private fixture must be reported as a skip, never as a pass and never as a failure: this checks the conversion
/// the skippable attributes rely on, and the lookup of fixtures.</summary>
public class SkippableFactTests
{
    private sealed class Recorder : IMessageBus
    {
        public List<IMessageSinkMessage> Messages { get; } = [];
        public bool QueueMessage(IMessageSinkMessage message) { Messages.Add(message); return true; }
        public void Dispose() { }
    }

    private static ITest StubTest()
    {
        var type = typeof(SkippableFactTests);
        var collection = new TestCollection(new TestAssembly(Reflector.Wrap(type.Assembly)), null, "stub collection");
        var method = new TestMethod(new TestClass(collection, Reflector.Wrap(type)), Reflector.Wrap(type.GetMethod(nameof(AMissingFixtureIsASkipExceptionNamingTheVariable))!));
        return new XunitTest(new XunitTestCase(new NullMessageSink(), TestMethodDisplay.Method, TestMethodDisplayOptions.None, method), "stub");
    }

    private static TestFailed Failed(params string[] exceptionTypes) =>
        new(StubTest(), 0m, "", exceptionTypes, exceptionTypes.Select(type => "message of " + type).ToArray(),
            exceptionTypes.Select(_ => "").ToArray(), exceptionTypes.Select((_, index) => index - 1).ToArray());

    [Fact]
    public void ASkipExceptionFailureBecomesASkipWithItsReason()
    {
        var inner = new Recorder();
        var bus = new SkipConvertingBus(inner);
        bus.QueueMessage(Failed(typeof(SkipException).FullName!));
        var skipped = Assert.IsAssignableFrom<ITestSkipped>(Assert.Single(inner.Messages));
        Assert.Equal("message of " + typeof(SkipException).FullName, skipped.Reason);
        var summary = bus.Adjust(new RunSummary { Total = 1, Failed = 1 });
        Assert.Equal((1, 0, 1), (summary.Total, summary.Failed, summary.Skipped));
    }

    [Fact]
    public void ASkipExceptionWrappedByAFixtureFailureBecomesASkip()
    {
        var inner = new Recorder();
        new SkipConvertingBus(inner).QueueMessage(Failed("Xunit.Sdk.TestClassException", typeof(SkipException).FullName!));
        Assert.IsAssignableFrom<ITestSkipped>(Assert.Single(inner.Messages));
    }

    [Theory]
    [InlineData("Xunit.Sdk.EqualException")]
    [InlineData("System.IO.FileNotFoundException")]
    public void AnyOtherFailureStaysAFailure(string exceptionType)
    {
        var inner = new Recorder();
        var bus = new SkipConvertingBus(inner);
        bus.QueueMessage(Failed(exceptionType));
        Assert.IsAssignableFrom<ITestFailed>(Assert.Single(inner.Messages));
        var summary = bus.Adjust(new RunSummary { Total = 1, Failed = 1 });
        Assert.Equal((1, 0), (summary.Failed, summary.Skipped));
    }

    [Fact]
    public void AMissingFixtureIsASkipExceptionNamingTheVariable()
    {
        var exception = Assert.Throws<SkipException>(() => TestFiles.Find("no-such-fixture-file.rvt"));
        Assert.Contains(TestFiles.EnvironmentVariable, exception.Message);
        Assert.Contains("no-such-fixture-file.rvt", exception.Message);
        Assert.Null(TestFiles.TryFindRelative("TestData/no-such-block.bin"));
    }
}
