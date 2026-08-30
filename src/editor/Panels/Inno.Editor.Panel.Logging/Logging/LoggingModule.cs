using System;

using Inno.Core.Diagnose;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.PlayMode;

namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Connects the editor Console to the independent logging and diagnostics cores.
/// </summary>
[EditorModule("diagnostics-logging", order: int.MinValue)]
internal sealed class LoggingModule : EditorModule
{
    private readonly IEditorPlayMode m_playMode;
    private readonly EditorDiagnosticBuffer m_diagnostics = new();

    private bool m_playSessionActive;
    private bool m_playSessionReachedPlaying;
    private bool m_started;

    internal LoggingModule(IEditorPlayMode playMode)
    {
        m_playMode = playMode ?? throw new ArgumentNullException(nameof(playMode));
    }

    /// <summary>
    /// Gets the rolling editor log buffer.
    /// </summary>
    internal EditorLogBuffer logs { get; } = new();

    internal EditorDiagnosticBuffer diagnostics => m_diagnostics;

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        if (m_started)
            return;
        LogManager.RegisterSink(logs);
        DiagnosticManager.RegisterSink(m_diagnostics);
        m_playMode.stateChanged += OnPlayModeStateChanged;
        m_started = true;
        if (m_playMode.state != EditorPlayModeState.Editing)
            BeginPlaySession(m_playMode.state == EditorPlayModeState.Playing);
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        if (!m_started)
            return;
        Detach();
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        if (m_started)
            Detach();
    }

    private void OnPlayModeStateChanged(EditorPlayModeState state)
    {
        switch (state)
        {
            case EditorPlayModeState.EnteringPlay:
                BeginPlaySession(reachedPlaying: false);
                break;
            case EditorPlayModeState.Playing:
                m_playSessionReachedPlaying = true;
                break;
            case EditorPlayModeState.Editing:
                CompletePlaySession();
                break;
        }
    }

    private void BeginPlaySession(bool reachedPlaying)
    {
        if (m_playSessionActive)
        {
            m_playSessionReachedPlaying |= reachedPlaying;
            return;
        }
        LogManager.Flush();
        logs.BeginPlaySession();
        m_playSessionActive = true;
        m_playSessionReachedPlaying = reachedPlaying;
    }

    private void CompletePlaySession()
    {
        if (!m_playSessionActive)
            return;
        LogManager.Flush();
        if (m_playSessionReachedPlaying)
            _ = logs.CompletePlaySession();
        else
            logs.CancelPlaySession();
        m_playSessionActive = false;
        m_playSessionReachedPlaying = false;
    }

    private void Detach()
    {
        m_playMode.stateChanged -= OnPlayModeStateChanged;
        try
        {
            CompletePlaySession();
        }
        finally
        {
            LogManager.UnregisterSink(logs);
            DiagnosticManager.UnregisterSink(m_diagnostics);
            m_started = false;
        }
    }
}
