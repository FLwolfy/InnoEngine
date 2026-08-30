using System;
using System.IO;

using Inno.Core.Assemblies;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Panel.Logging;

using Xunit;

namespace Inno.Editor.PlayMode.Tests;

public sealed class PlayModeLogRetentionTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorPlayModeLogTests",
        Guid.NewGuid().ToString("N"));

    private readonly EditorContext m_context;
    private readonly FakePlayMode m_playMode = new();
    private readonly LoggingModule m_logging;

    public PlayModeLogRetentionTests()
    {
        Directory.CreateDirectory(m_projectRoot);
        LogManager.Initialize();
        LogManager.SetMinimumLevel(LogLevel.Debug);
        m_context = new EditorContext(m_projectRoot);
        m_logging = new LoggingModule(m_playMode);
        m_logging.Start(m_context);
    }

    public void Dispose()
    {
        m_logging.Stop(m_context);
        ((IDisposable)m_logging).Dispose();
        LogManager.Shutdown();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void CompletedPlaySessionRemovesOnlyTransientRuntimeEntries()
    {
        Dispatch(LogLevel.Info, AssemblyScope.Runtime, "before-play");
        m_playMode.SetState(EditorPlayModeState.EnteringPlay);
        Dispatch(LogLevel.Debug, AssemblyScope.Runtime, "runtime-debug");
        Dispatch(LogLevel.Info, AssemblyScope.Runtime, "runtime-info");
        Dispatch(LogLevel.Warn, AssemblyScope.Runtime, "runtime-warning");
        Dispatch(LogLevel.Error, AssemblyScope.Runtime, "runtime-error");
        Dispatch(LogLevel.Fatal, AssemblyScope.Runtime, "runtime-fatal");
        Dispatch(LogLevel.Info, AssemblyScope.Editor, "editor-info");
        m_playMode.SetState(EditorPlayModeState.Playing);
        m_playMode.SetState(EditorPlayModeState.ExitingPlay);
        m_playMode.SetState(EditorPlayModeState.Editing);

        LogEntry[] entries = m_logging.logs.Snapshot();

        Assert.Contains(entries, static entry => entry.message == "before-play");
        Assert.DoesNotContain(entries, static entry => entry.message == "runtime-debug");
        Assert.DoesNotContain(entries, static entry => entry.message == "runtime-info");
        Assert.Contains(entries, static entry => entry.message == "runtime-warning");
        Assert.Contains(entries, static entry => entry.message == "runtime-error");
        Assert.Contains(entries, static entry => entry.message == "runtime-fatal");
        Assert.Contains(entries, static entry => entry.message == "editor-info");
    }

    [Fact]
    public void CancelledEntryRetainsLogsBecauseSimulationNeverStarted()
    {
        m_playMode.SetState(EditorPlayModeState.EnteringPlay);
        Dispatch(LogLevel.Info, AssemblyScope.Runtime, "entry-diagnostic");
        m_playMode.SetState(EditorPlayModeState.Editing);

        Assert.Contains(
            m_logging.logs.Snapshot(),
            static entry => entry.message == "entry-diagnostic");
    }

    private static void Dispatch(LogLevel level, AssemblyScope scope, string message)
        => LogManager.Dispatch(new LogEntry(
            level,
            AssemblyDomain.InnoScripting,
            scope,
            "PlayModeLogRetentionTests",
            message,
            nameof(PlayModeLogRetentionTests),
            0));

    private sealed class FakePlayMode : IEditorPlayMode
    {
        public EditorPlayModeState state { get; private set; }
        public bool isPlaying => state == EditorPlayModeState.Playing;
        public string? lastFailure => null;

        public event Action<EditorPlayModeState>? stateChanged;

        public bool EnterPlayMode() => false;

        public bool ExitPlayMode() => false;

        internal void SetState(EditorPlayModeState value)
        {
            state = value;
            stateChanged?.Invoke(value);
        }
    }
}
