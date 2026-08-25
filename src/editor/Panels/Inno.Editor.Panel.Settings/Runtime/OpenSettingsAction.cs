using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.Settings;

[EditorAction("editor.settings.open")]
[EditorMenu("editor/main-menu", "Edit/Settings...", order: 1000)]
internal sealed class OpenSettingsAction(SettingsWindowModule window) : EditorAction
{
    /// <inheritdoc />
    protected override void Execute(EditorActionContext context)
        => window.Open();
}
