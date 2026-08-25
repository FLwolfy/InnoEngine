using Inno.Editor.Core;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Settings;

[EditorModule(order: 20)]
internal sealed class SettingsWindowModule(EditorSettings settings) : EditorModule
{
    private SettingsEditSession? m_session;

    internal bool isVisible { get; private set; }

    internal SettingsEditSession? session => m_session;

    internal void Open()
    {
        if (!isVisible)
            m_session = new SettingsEditSession(settings);
        isVisible = true;
    }

    internal void Close()
    {
        isVisible = false;
        m_session = null;
    }

    internal void Refresh()
    {
        m_session = new SettingsEditSession(settings);
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
        => Close();
}
