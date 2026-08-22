using System;

using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Panel.Logging;

/// <summary>
/// Connects the editor Console to the independent logging and diagnostics cores.
/// </summary>
[EditorModule(order: int.MinValue)]
public sealed class LoggingModule : EditorModule, IDisposable
{
    private readonly EditorDiagnosticBuffer m_diagnostics = new();
    private bool m_started;

    /// <summary>
    /// Gets the rolling editor log buffer.
    /// </summary>
    public EditorLogBuffer logs { get; } = new();

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
    public void Dispose()
    {
        if (m_started)
        {
            LogManager.UnregisterSink(logs);
            DiagnosticManager.UnregisterSink(m_diagnostics);
            m_started = false;
        }
        GC.SuppressFinalize(this);
    }
}
