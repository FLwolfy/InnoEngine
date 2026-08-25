using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Describes project layer definitions and interaction Settings.
/// </summary>
[EditorSettingPath("Project/Layers")]
internal sealed class LayerSettingsPage : EditorSetting
{
    /// <inheritdoc />
    public override string description
        => "Define the named layer slots used by scene objects, rendering, physics, and queries.";
}
