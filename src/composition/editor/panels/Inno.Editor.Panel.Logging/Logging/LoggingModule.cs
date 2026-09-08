using System;

using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Diagnostics;
using Inno.Editor.PlayMode;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Logging;

[EditorModule("diagnostics-logging", order: int.MinValue)]
internal sealed class LoggingModule : EditorModule, IEditorConsole
{
    private readonly EditorConsole m_console;
    private readonly EditorSettings m_settings;

    internal LoggingModule(
        LogRouter logRouter,
        DiagnosticHub diagnosticHub,
        IEditorPlayMode playMode,
        EditorSettings settings)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_console = new EditorConsole(logRouter, diagnosticHub, playMode);
    }

    /// <summary>
    /// Initializes this feature after its owning runtime has activated all required services.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void OnStart(EditorContext context)
    {
        ApplySettings(m_settings);
        m_settings.changed += ApplySettings;
        m_console.Start();
    }

    /// <summary>
    /// Stops this feature before its owning runtime releases generation-scoped services.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void OnStop(EditorContext context)
    {
        m_settings.changed -= ApplySettings;
        m_console.Dispose();
    }

    /// <summary>
    /// Releases resources retained by this feature after it has stopped.
    /// </summary>
    protected override void OnDispose()
        => m_console.Dispose();

    private void ApplySettings(EditorSettings settings)
        => m_console.clearOnPlay = ClearConsoleOnPlaySetting.Read(settings);

    int IEditorConsole.capacity
    {
        get => m_console.capacity;
        set => m_console.capacity = value;
    }

    bool IEditorConsole.clearOnPlay
    {
        get => m_console.clearOnPlay;
        set => m_console.clearOnPlay = value;
    }

    EditorConsoleSnapshot IEditorConsole.Capture()
        => m_console.Capture();

    void IEditorConsole.Clear()
        => m_console.Clear();
}
