using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnostics;

using Xunit;

namespace Inno.Core.Diagnostics.Tests;

[CollectionDefinition("Diagnostics", DisableParallelization = true)]
public sealed class DiagnosticsCollection;

[Collection("Diagnostics")]
public sealed class DiagnosticManagerTests : IDisposable
{
    private static readonly DiagnosticSource SOURCE = new("tests.compiler", "Test Compiler");
    private static readonly DiagnosticSource OTHER_SOURCE = new("tests.importer", "Test Importer");

    [Fact]
    public void Publish_ReplacesTheCompleteReportOwnedByOneSource()
    {
        var sink = new ProbeSink();
        DiagnosticManager.RegisterSink(sink);

        DiagnosticManager.Publish(SOURCE,
        [
            new Diagnostic(
                DiagnosticSeverity.Error,
                "CS1001",
                "Expected identifier.",
                new DiagnosticLocation("Assets/Test.cs", 4, 2))
        ]);
        Diagnostic first = Assert.Single(sink.Get(SOURCE));
        Assert.Equal("CS1001", first.code);
        Assert.Equal("Assets/Test.cs", first.location?.sourcePath);

        DiagnosticManager.Publish(SOURCE,
        [
            new Diagnostic(
                DiagnosticSeverity.Warning,
                "CS0168",
                "Variable is declared but never used.")
        ]);

        Diagnostic replacement = Assert.Single(sink.Get(SOURCE));
        Assert.Equal("CS0168", replacement.code);
        Assert.Equal(DiagnosticSeverity.Warning, replacement.severity);
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void Publish_EmptyCollectionClearsOnlyItsSource()
    {
        var sink = new ProbeSink();
        DiagnosticManager.RegisterSink(sink);
        DiagnosticManager.Publish(SOURCE, [new Diagnostic(DiagnosticSeverity.Error, "A", "First")]);
        DiagnosticManager.Publish(OTHER_SOURCE, [new Diagnostic(DiagnosticSeverity.Warning, "B", "Second")]);

        DiagnosticManager.Publish(SOURCE, []);

        Assert.Empty(sink.Get(SOURCE));
        Assert.Single(sink.Get(OTHER_SOURCE));
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void RegisterSink_ReplaysEveryActiveReport()
    {
        DiagnosticManager.Publish(SOURCE, [new Diagnostic(DiagnosticSeverity.Error, "A", "First")]);
        DiagnosticManager.Publish(OTHER_SOURCE, [new Diagnostic(DiagnosticSeverity.Warning, "B", "Second")]);
        var sink = new ProbeSink();

        DiagnosticManager.RegisterSink(sink);

        Assert.Single(sink.Get(SOURCE));
        Assert.Single(sink.Get(OTHER_SOURCE));
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void SinkFailure_DoesNotPreventOtherSinksFromReceivingState()
    {
        var failingSink = new FailingSink();
        var sink = new ProbeSink();
        DiagnosticManager.RegisterSink(failingSink);
        DiagnosticManager.RegisterSink(sink);

        DiagnosticManager.Publish(SOURCE, [new Diagnostic(DiagnosticSeverity.Error, "A", "Visible")]);

        Assert.Single(sink.Get(SOURCE));
        DiagnosticManager.UnregisterSink(failingSink);
        DiagnosticManager.UnregisterSink(sink);
    }

    public void Dispose()
    {
        DiagnosticManager.Clear(SOURCE);
        DiagnosticManager.Clear(OTHER_SOURCE);
    }

    private sealed class ProbeSink : IDiagnosticSink
    {
        private readonly Dictionary<string, Diagnostic[]> m_reports = new(StringComparer.Ordinal);

        internal Diagnostic[] Get(DiagnosticSource source)
            => m_reports.TryGetValue(source.id, out Diagnostic[]? diagnostics)
                ? diagnostics
                : [];

        public void Replace(DiagnosticReport report)
            => m_reports[report.source.id] = report.diagnostics.ToArray();

        public void Clear(DiagnosticSource source)
            => m_reports.Remove(source.id);
    }

    private sealed class FailingSink : IDiagnosticSink
    {
        public void Replace(DiagnosticReport report)
            => throw new InvalidOperationException("Expected test failure.");

        public void Clear(DiagnosticSource source)
            => throw new InvalidOperationException("Expected test failure.");
    }
}
