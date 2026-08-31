using System;

using Inno.Core.Diagnose;
using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Connects the editor Console to the independent logging and diagnostics cores.
/// </summary>
[EditorModule("diagnostics-logging", order: int.MinValue)]
internal sealed class LoggingModule : EditorModule
{
    private readonly EditorDiagnosticBuffer m_diagnostics = new();
    private bool m_started;

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
        m_started = true;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        if (!m_started)
            return;
        LogManager.UnregisterSink(logs);
        DiagnosticManager.UnregisterSink(m_diagnostics);
        m_started = false;
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        if (m_started)
        {
            LogManager.UnregisterSink(logs);
            DiagnosticManager.UnregisterSink(m_diagnostics);
            m_started = false;
        }
    }
}
