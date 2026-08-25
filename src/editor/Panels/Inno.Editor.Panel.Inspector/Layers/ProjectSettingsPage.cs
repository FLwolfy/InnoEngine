using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Describes Settings that affect the active game project.
/// </summary>
[EditorSettingPath("Project")]
internal sealed class ProjectSettingsPage : EditorSetting
{
    /// <inheritdoc />
    public override string description
        => "Configure runtime-facing systems and content conventions for this project.";
}
