using Inno.Editor.Settings;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Exporting;

[EditorSettingPath("Editor/Export")]
internal sealed class ExportSettingsPage : EditorSetting
{
    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Configure deterministic Plugin packages and source-free Player builds.";
}

[EditorSettingPath("Editor/Export/Plugin")]
internal sealed class PluginExportSettingsPage : EditorSetting
{
    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Choose how installed Plugin dependencies are represented in exported packages.";
}

[EditorSettingPath(C_PATH)]
internal sealed class EmbedPluginDependenciesSetting : EditorSetting
{
    internal const string C_PATH = "Editor/Export/Plugin/Embed Dependencies";

    /// <summary>
    /// Gets a new value initialized to this setting's canonical default state.
    /// </summary>
    public override EditorSettingObject defaultValue
    {
        get
        {
            var result = new EditorSettingObject();
            result.SetAsBoolean("value", false);
            return result;
        }
    }

    /// <summary>
    /// Gets the presentation section that groups this setting.
    /// </summary>
    public override string section => "Package Composition";

    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Embed complete installed dependency packages so the exported ZIP can be installed by itself.";

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The mutable editor setting value currently being presented.
    /// </param>
    protected override void OnDraw(EditorSettingObject setting)
    {
        bool value = setting.GetAsBoolean("value");
        if (NativeImGui.Checkbox("Include dependency Plugin packages", ref value))
            setting.SetAsBoolean("value", value);
    }

    internal static bool Read(EditorSettings settings)
        => settings.Get(C_PATH).GetAsBoolean("value");
}
