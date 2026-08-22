using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnose;

using Xunit;

namespace Inno.Core.Diagnose.Tests;

[CollectionDefinition("Diagnose", DisableParallelization = true)]
public sealed class DiagnoseCollection;

[Collection("Diagnose")]
public sealed class DiagnosticManagerTests : IDisposable
{
    private const string C_COMPILATION_DIAGNOSTICS = "Compilation";
    private const string C_IMPORT_DIAGNOSTICS = "Import";
    private const string C_RELOAD_DIAGNOSTICS = "Reload";

    private readonly Guid m_assetId = Guid.NewGuid();

    [Fact]
    public void DiagnosticFactories_CreateTheRequestedSeverityAndLocation()
    {
        var location = new DiagnosticLocation("Assets/Test.cs", 4, 2);

        Diagnostic info = Diagnostic.Info("I", "Info", location);
        Diagnostic warning = Diagnostic.Warning("W", "Warning");
        Diagnostic error = Diagnostic.Error("E", "Error");

        Assert.Equal(DiagnosticSeverity.Info, info.severity);
        Assert.Equal(location, info.location);
        Assert.Equal(DiagnosticSeverity.Warning, warning.severity);
        Assert.Equal(DiagnosticSeverity.Error, error.severity);
    }

    [Fact]
    public void Set_ReplacesTheCompleteReportOwnedByOneCallerGroup()
    {
        var sink = new ProbeSink();
        DiagnosticManager.RegisterSink(sink);

        Diagnostics.Set(
            C_COMPILATION_DIAGNOSTICS,
            Diagnostic.Error(
                "CS1001",
                "Expected identifier.",
                new DiagnosticLocation("Assets/Test.cs", 4, 2)));
        DiagnosticReport firstReport = Assert.Single(sink.reports);
        Diagnostic first = Assert.Single(firstReport.diagnostics);
        Assert.Equal("CS1001", first.code);
        Assert.Equal("Assets/Test.cs", first.location?.sourcePath);

        Diagnostics.Set(
            C_COMPILATION_DIAGNOSTICS,
            Diagnostic.Warning("CS0168", "Variable is declared but never used."));

        DiagnosticReport replacementReport = Assert.Single(sink.reports);
        Diagnostic replacement = Assert.Single(replacementReport.diagnostics);
        Assert.Equal("CS0168", replacement.code);
        Assert.Equal(DiagnosticSeverity.Warning, replacement.severity);
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void Set_EmptyCollectionClearsOnlyItsCallerGroup()
    {
        var sink = new ProbeSink();
        DiagnosticManager.RegisterSink(sink);
        Diagnostics.Set(C_COMPILATION_DIAGNOSTICS, Diagnostic.Error("A", "First"));
        Diagnostics.Set(C_RELOAD_DIAGNOSTICS, Diagnostic.Warning("B", "Second"));

        Diagnostics.Set(C_COMPILATION_DIAGNOSTICS, Array.Empty<Diagnostic>());

        DiagnosticReport remaining = Assert.Single(sink.reports);
        Assert.Equal(C_RELOAD_DIAGNOSTICS, remaining.source.displayName);
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void Set_TargetedReportsRemainIndependent()
    {
        var sink = new ProbeSink();
        Guid otherAssetId = Guid.NewGuid();
        DiagnosticManager.RegisterSink(sink);
        Diagnostics.Set(
            m_assetId,
            C_IMPORT_DIAGNOSTICS,
            Diagnostic.Error("A", "First asset"),
            displayName: "Assets/First.asset");
        Diagnostics.Set(
            otherAssetId,
            C_IMPORT_DIAGNOSTICS,
            Diagnostic.Warning("B", "Second asset"),
            displayName: "Assets/Second.asset");

        Diagnostics.Clear(m_assetId, C_IMPORT_DIAGNOSTICS);

        DiagnosticReport remaining = Assert.Single(sink.reports);
        Assert.Equal("Assets/Second.asset", remaining.source.displayName);
        Diagnostics.Clear(otherAssetId, C_IMPORT_DIAGNOSTICS);
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void Set_SameGroupNameRemainsIndependentAcrossCallerTypes()
    {
        var sink = new ProbeSink();
        DiagnosticManager.RegisterSink(sink);
        Diagnostics.Set(C_COMPILATION_DIAGNOSTICS, Diagnostic.Error("A", "Primary caller"));
        OtherProducer.SetCompilation();

        Assert.Equal(2, sink.reports.Count);

        OtherProducer.ClearCompilation();
        Assert.Single(sink.reports);
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void RegisterSink_ReplaysEveryActiveReport()
    {
        Diagnostics.Set(C_COMPILATION_DIAGNOSTICS, Diagnostic.Error("A", "First"));
        Diagnostics.Set(C_RELOAD_DIAGNOSTICS, Diagnostic.Warning("B", "Second"));
        var sink = new ProbeSink();

        DiagnosticManager.RegisterSink(sink);

        Assert.Equal(2, sink.reports.Count);
        DiagnosticManager.UnregisterSink(sink);
    }

    [Fact]
    public void SinkFailure_DoesNotPreventOtherSinksFromReceivingState()
    {
        var failingSink = new FailingSink();
        var sink = new ProbeSink();
        DiagnosticManager.RegisterSink(failingSink);
        DiagnosticManager.RegisterSink(sink);

        Diagnostics.Set(C_COMPILATION_DIAGNOSTICS, Diagnostic.Error("A", "Visible"));

        Assert.Single(sink.reports);
        DiagnosticManager.UnregisterSink(failingSink);
        DiagnosticManager.UnregisterSink(sink);
    }

    public void Dispose()
    {
        Diagnostics.Clear(C_COMPILATION_DIAGNOSTICS);
        Diagnostics.Clear(C_RELOAD_DIAGNOSTICS);
        Diagnostics.Clear(m_assetId, C_IMPORT_DIAGNOSTICS);
    }

    private sealed class ProbeSink : IDiagnosticSink
    {
        private readonly Dictionary<string, DiagnosticReport> m_reports = new(StringComparer.Ordinal);

        internal IReadOnlyList<DiagnosticReport> reports => m_reports.Values.ToArray();

        public void Replace(DiagnosticReport report)
            => m_reports[report.source.id] = report;

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

    private static class OtherProducer
    {
        internal static void SetCompilation()
            => Diagnostics.Set(
                C_COMPILATION_DIAGNOSTICS,
                Diagnostic.Error("B", "Other caller"));

        internal static void ClearCompilation()
            => Diagnostics.Clear(C_COMPILATION_DIAGNOSTICS);
    }
}
