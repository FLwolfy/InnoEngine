using System;
using System.Linq;

using Inno.Extensibility.Modules;
using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Editor.Diagnostics;

using Xunit;

namespace Inno.Editor.PlayMode.Tests;

public sealed class PlayModeLogRetentionTests : IDisposable
{
    private readonly FakePlayMode m_playMode = new();
    private readonly DiagnosticHub m_diagnosticHub = new();
    private readonly LogRouter m_logRouter = new();
    private readonly IDisposable m_diagnosticScope;
    private readonly IDisposable m_logScope;
    private readonly EditorConsole m_console;

    public PlayModeLogRetentionTests()
    {
        m_logScope = m_logRouter.EnterScope();
        m_diagnosticScope = m_diagnosticHub.EnterScope();
        m_logRouter.SetMinimumLevel(LogLevel.Debug);
        m_console = new EditorConsole(m_logRouter, m_diagnosticHub, m_playMode);
        m_console.Start();
    }

    public void Dispose()
    {
        m_console.Dispose();
        m_diagnosticScope.Dispose();
        m_logScope.Dispose();
        m_logRouter.Dispose();
    }

    [Fact]
    public void ClearOnPlayRemovesOldLogsButRetainsCurrentDiagnostics()
    {
        Assert.True(m_console.clearOnPlay);
        Dispatch(LogLevel.Info, LogSessionId.none, "before-play");
        m_diagnosticHub.Set(
            new DiagnosticSource("test.compiler", "Test Compiler"),
            [Diagnostic.Error("TEST001", "compiler-diagnostic")]);
        m_logRouter.Flush();

        _ = m_playMode.Begin();

        EditorConsoleOccurrence[] entries = m_console.Capture().occurrences.ToArray();

        Assert.DoesNotContain(entries, static entry => entry.message == "before-play");
        Assert.Contains(entries, static entry => entry.message == "compiler-diagnostic");
    }

    [Fact]
    public void CompletedPlaySessionRetainsEveryRuntimeSeverity()
    {
        LogSessionId sessionId = m_playMode.Begin();
        Dispatch(LogLevel.Debug, sessionId, "runtime-debug");
        Dispatch(LogLevel.Info, sessionId, "runtime-info");
        Dispatch(LogLevel.Warn, sessionId, "runtime-warning");
        Dispatch(LogLevel.Error, sessionId, "runtime-error");
        Dispatch(LogLevel.Fatal, sessionId, "runtime-fatal");
        m_playMode.SetState(EditorPlayModeState.Playing);
        m_playMode.SetState(EditorPlayModeState.Stopping);
        m_playMode.End();
        m_logRouter.Flush();

        EditorConsoleOccurrence[] entries = m_console.Capture().occurrences.ToArray();

        Assert.Contains(entries, static entry => entry.message == "runtime-debug");
        Assert.Contains(entries, static entry => entry.message == "runtime-info");
        Assert.Contains(entries, static entry => entry.message == "runtime-warning");
        Assert.Contains(entries, static entry => entry.message == "runtime-error");
        Assert.Contains(entries, static entry => entry.message == "runtime-fatal");
    }

    [Fact]
    public void FailedEntryRetainsPreparationDiagnosticsBecauseSimulationNeverStarted()
    {
        LogSessionId sessionId = m_playMode.Begin();
        Dispatch(LogLevel.Info, sessionId, "entry-diagnostic");
        m_playMode.SetState(EditorPlayModeState.Failed);
        m_logRouter.Flush();

        Assert.Contains(
            m_console.Capture().occurrences,
            static entry => entry.message == "entry-diagnostic");
    }

    [Fact]
    public void DisabledClearOnPlayRetainsExistingLogs()
    {
        m_console.clearOnPlay = false;
        Dispatch(LogLevel.Info, LogSessionId.none, "before-play");
        m_logRouter.Flush();

        _ = m_playMode.Begin();

        Assert.Contains(
            m_console.Capture().occurrences,
            static entry => entry.message == "before-play");
    }

    [Fact]
    public void CollapseGroupsEquivalentNonConsecutiveOccurrencesGlobally()
    {
        Dispatch(LogLevel.Info, LogSessionId.none, "repeated", file: "A.cs", line: 10);
        Dispatch(LogLevel.Warn, LogSessionId.none, "middle", file: "B.cs", line: 20);
        Dispatch(LogLevel.Info, LogSessionId.none, "repeated", file: "A.cs", line: 10);
        Dispatch(LogLevel.Info, LogSessionId.none, "repeated", file: "C.cs", line: 30);
        m_logRouter.Flush();

        EditorConsoleSnapshot snapshot = m_console.Capture();
        EditorConsoleGroup repeated = Assert.Single(snapshot.groups.Where(
            static group => group.latest.message == "repeated" && group.latest.file == "A.cs"));

        Assert.Equal(2, repeated.count);
        Assert.Equal(3, snapshot.groups.Count);
        Assert.True(repeated.occurrences[0].sequence < repeated.occurrences[1].sequence);
    }

    [Fact]
    public void CollapseDoesNotMergeEquivalentEntriesAcrossRuntimeSessions()
    {
        LogSessionId first = LogSessionId.Create();
        LogSessionId second = LogSessionId.Create();
        Dispatch(LogLevel.Info, first, "session-entry", file: "A.cs", line: 10);
        Dispatch(LogLevel.Info, second, "session-entry", file: "A.cs", line: 10);
        m_logRouter.Flush();

        EditorConsoleSnapshot snapshot = m_console.Capture();

        Assert.Equal(
            2,
            snapshot.groups.Count(static group => group.latest.message == "session-entry"));
    }

    [Fact]
    public void ConsoleTimelineAppendsTheNewestEntryAtTheBottom()
    {
        Dispatch(LogLevel.Info, LogSessionId.none, "first");
        Dispatch(LogLevel.Error, LogSessionId.none, "latest");
        m_logRouter.Flush();

        EditorConsoleSnapshot snapshot = m_console.Capture();

        Assert.Equal("first", snapshot.occurrences[0].message);
        Assert.Equal("latest", snapshot.occurrences[^1].message);
        Assert.Equal("first", snapshot.groups[0].latest.message);
        Assert.Equal("latest", snapshot.groups[^1].latest.message);
    }

    private void Dispatch(
        LogLevel level,
        LogSessionId sessionId,
        string message,
        string file = nameof(PlayModeLogRetentionTests),
        int line = 0)
        => m_logRouter.Dispatch(new LogEntry(
            level,
            AssemblyDomain.InnoScripting,
            AssemblyScope.Runtime,
            "PlayModeLogRetentionTests",
            message,
            file,
            line,
            $"at {file}:{line}",
            sessionId));

    private sealed class FakePlayMode : IEditorPlayMode
    {
        public EditorPlayModeState state { get; private set; }

        public bool isPlaying => state == EditorPlayModeState.Playing;

        public string? lastFailure => null;

        public LogSessionId activeSessionId { get; private set; }

        public event Action<EditorPlayModeState>? stateChanged;

        public bool EnterPlayMode() => false;

        public bool ExitPlayMode() => false;

        internal LogSessionId Begin()
        {
            activeSessionId = LogSessionId.Create();
            SetState(EditorPlayModeState.Compiling);
            return activeSessionId;
        }

        internal void End()
        {
            SetState(EditorPlayModeState.Editing);
            activeSessionId = LogSessionId.none;
        }

        internal void SetState(EditorPlayModeState value)
        {
            state = value;
            stateChanged?.Invoke(value);
        }
    }
}
