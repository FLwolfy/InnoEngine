using Inno.Editor.Core;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Settings;

[EditorModule("settings-window", order: 20)]
internal sealed class SettingsWindowModule(
    EditorSettings editorSettings,
    ProjectSettingsEditor projectSettings) : EditorModule
{
    private SettingsEditSession? m_session;

    internal bool isVisible { get; private set; }

    internal SettingsEditSession? session => m_session;

    internal void Open()
    {
        if (!isVisible)
            m_session = new SettingsEditSession(editorSettings, projectSettings);
        isVisible = true;
    }

    internal void Close()
    {
        isVisible = false;
        m_session = null;
    }

    internal void Refresh()
    {
        m_session = new SettingsEditSession(editorSettings, projectSettings);
    }

    /// <summary>
    /// Stops this feature before its owning runtime releases the active generation.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnStop(EditorContext context)
        => Close();
}
