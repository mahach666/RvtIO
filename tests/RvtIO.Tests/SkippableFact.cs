using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace RvtIO.Tests;

/// <summary>Thrown by a test (usually through <see cref="TestFiles"/>) when its input is not available on this machine. A test
/// marked <see cref="SkippableFactAttribute"/> or <see cref="SkippableTheoryAttribute"/> that throws it is reported as skipped,
/// with the message as the reason, instead of failed. Plain xunit 2.9 has no runtime skip, hence this small pair.</summary>
public sealed class SkipException(string reason) : Exception(reason);

/// <summary>A fact that is reported as skipped when it throws <see cref="SkipException"/>.</summary>
[XunitTestCaseDiscoverer("RvtIO.Tests.SkippableFactDiscoverer", "RvtIO.Tests")]
public sealed class SkippableFactAttribute : FactAttribute;

/// <summary>A theory whose rows are reported as skipped when they throw <see cref="SkipException"/>.</summary>
[XunitTestCaseDiscoverer("RvtIO.Tests.SkippableTheoryDiscoverer", "RvtIO.Tests")]
public sealed class SkippableTheoryAttribute : TheoryAttribute;

public sealed class SkippableFactDiscoverer(IMessageSink diagnosticMessageSink) : IXunitTestCaseDiscoverer
{
    private readonly FactDiscoverer _inner = new(diagnosticMessageSink);

    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions options, ITestMethod testMethod, IAttributeInfo factAttribute) =>
        _inner.Discover(options, testMethod, factAttribute).Select(testCase => testCase is ExecutionErrorTestCase
            ? testCase
            : new SkippableTestCase(diagnosticMessageSink, options.MethodDisplayOrDefault(), options.MethodDisplayOptionsOrDefault(),
                testCase.TestMethod, testCase.TestMethodArguments));
}

public sealed class SkippableTheoryDiscoverer(IMessageSink diagnosticMessageSink) : IXunitTestCaseDiscoverer
{
    private readonly TheoryDiscoverer _inner = new(diagnosticMessageSink);

    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions options, ITestMethod testMethod, IAttributeInfo theoryAttribute)
    {
        var display = options.MethodDisplayOrDefault();
        var displayOptions = options.MethodDisplayOptionsOrDefault();
        foreach (var testCase in _inner.Discover(options, testMethod, theoryAttribute))
            yield return testCase switch
            {
                XunitTheoryTestCase => new SkippableTheoryTestCase(diagnosticMessageSink, display, displayOptions, testCase.TestMethod),
                ExecutionErrorTestCase or XunitSkippedDataRowTestCase => testCase,
                _ => new SkippableTestCase(diagnosticMessageSink, display, displayOptions, testCase.TestMethod, testCase.TestMethodArguments),
            };
    }
}

public sealed class SkippableTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer", error: true)]
    public SkippableTestCase() { }

    public SkippableTestCase(IMessageSink sink, TestMethodDisplay display, TestMethodDisplayOptions displayOptions, ITestMethod method, object[]? arguments = null)
        : base(sink, display, displayOptions, method, arguments) { }

    public override async Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments,
        ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
    {
        var bus = new SkipConvertingBus(messageBus);
        var summary = await base.RunAsync(diagnosticMessageSink, bus, constructorArguments, aggregator, cancellationTokenSource);
        return bus.Adjust(summary);
    }
}

public sealed class SkippableTheoryTestCase : XunitTheoryTestCase
{
    [Obsolete("Called by the de-serializer", error: true)]
    public SkippableTheoryTestCase() { }

    public SkippableTheoryTestCase(IMessageSink sink, TestMethodDisplay display, TestMethodDisplayOptions displayOptions, ITestMethod method)
        : base(sink, display, displayOptions, method) { }

    public override async Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments,
        ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
    {
        var bus = new SkipConvertingBus(messageBus);
        var summary = await base.RunAsync(diagnosticMessageSink, bus, constructorArguments, aggregator, cancellationTokenSource);
        return bus.Adjust(summary);
    }
}

/// <summary>Turns a failure caused by <see cref="SkipException"/> into a skip and counts how many it turned.</summary>
internal sealed class SkipConvertingBus(IMessageBus inner) : IMessageBus
{
    private int _skipped;

    public bool QueueMessage(IMessageSinkMessage message)
    {
        // A class fixture that throws in its constructor reports the SkipException wrapped in one xunit exception.
        if (message is ITestFailed failed && failed.ExceptionTypes.Length is 1 or 2 && failed.ExceptionTypes[^1] == typeof(SkipException).FullName)
        {
            Interlocked.Increment(ref _skipped);
            return inner.QueueMessage(new TestSkipped(failed.Test, failed.Messages[^1]));
        }
        return inner.QueueMessage(message);
    }

    public RunSummary Adjust(RunSummary summary)
    {
        summary.Failed -= _skipped;
        summary.Skipped += _skipped;
        return summary;
    }

    public void Dispose() { }
}
