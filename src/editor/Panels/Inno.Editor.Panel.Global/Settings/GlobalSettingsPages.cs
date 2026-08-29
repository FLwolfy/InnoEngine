using Inno.Editor.Settings;

namespace Inno.Editor.Panel.Global;

[EditorSettingPath("Editor")]
internal sealed class GlobalSettingsPage : EditorSetting
{
    public override string description
        => "Configure editor-wide appearance, behavior, and user-facing tools.";
}

[EditorSettingPath("Editor/Appearance")]
internal sealed class AppearanceSettingsPage : EditorSetting
{
    public override string description
        => "Customize editor appearance, scaling, and semantic presentation.";
}

[EditorSettingPath("Editor/Appearance/Icons")]
internal sealed class IconSettingsPage : EditorSetting
{
    public override string description
        => "Choose the glyph used for each semantic object throughout the editor.";
}
