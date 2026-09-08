using Inno.Editor.Settings;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Logging;

[EditorSettingPath("Editor/Diagnostics")]
internal sealed class DiagnosticsSettingsPage : EditorSetting
{
    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Configure diagnostic presentation and retention behavior for this editor.";
}

[EditorSettingPath("Editor/Diagnostics/Console")]
internal sealed class ConsoleSettingsPage : EditorSetting
{
    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Configure how the Console retains ordinary logs across editor workflows.";
}

[EditorSettingPath(C_PATH)]
internal sealed class ClearConsoleOnPlaySetting : EditorSetting
{
    internal const string C_PATH = "Editor/Diagnostics/Console/Clear on Play";

    /// <summary>
    /// Gets a new value initialized to the canonical Console retention policy.
    /// </summary>
    public override EditorSettingObject defaultValue
    {
        get
        {
            var result = new EditorSettingObject();
            result.SetAsBoolean("value", true);
            return result;
        }
    }

    /// <summary>
    /// Gets the presentation section that groups this setting.
    /// </summary>
    public override string section => "Play Mode";

    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Clear ordinary Console logs when a new Play Mode request begins while retaining active diagnostics.";

    /// <summary>
    /// Draws the staged Console retention preference.
    /// </summary>
    /// <param name="setting">
    /// The isolated mutable Settings object that owns the staged value.
    /// </param>
    protected override void OnDraw(EditorSettingObject setting)
    {
        bool value = setting.GetAsBoolean("value", true);
        if (NativeImGui.Checkbox("Clear ordinary logs when entering Play Mode", ref value))
            setting.SetAsBoolean("value", value);
    }

    internal static bool Read(EditorSettings settings)
        => settings.Get(C_PATH).GetAsBoolean("value", true);
}
