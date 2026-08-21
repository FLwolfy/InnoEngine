using System;

using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Panel.Logging;

/// <summary>Owns the editor diagnostic streams for one editor runtime.</summary>
[EditorModule(order: int.MinValue)]
public sealed class LoggingModule : EditorModule, IDisposable
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
