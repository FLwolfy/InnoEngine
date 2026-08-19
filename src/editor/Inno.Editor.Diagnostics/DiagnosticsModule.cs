using System;

using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Diagnostics.Logging;

namespace Inno.Editor.Diagnostics;

/// <summary>Owns the editor diagnostic streams for one editor runtime.</summary>
[EditorModule(order: 300)]
public sealed class DiagnosticsModule : EditorModule, IDisposable
{
    private bool m_started;

    /// <summary>Gets the rolling editor log buffer.</summary>
    public EditorLogBuffer logs { get; } = new();

    /// <inheritdoc />
    protected override void OnStart(EditorContext context)
    {
        if (m_started)
            return;
        LogManager.RegisterSink(logs);
        m_started = true;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        if (!m_started)
            return;
        LogManager.UnregisterSink(logs);
        m_started = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (m_started)
        {
            LogManager.UnregisterSink(logs);
            m_started = false;
        }
        GC.SuppressFinalize(this);
    }
}
