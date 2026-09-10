using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Settings;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Global;

[EditorSettingPath("Editor/Appearance/Density", order: 50)]
internal sealed class DensitySetting : EditorSetting
{
    /// <summary>
    /// Gets the comfortable default density.
    /// </summary>
    public override EditorSettingObject defaultValue => CreateDefault();

    /// <summary>
    /// Gets the appearance section containing this setting.
    /// </summary>
    public override string section => "Layout";

    /// <summary>
    /// Gets the user-facing density description.
    /// </summary>
    public override string description
        => "Use comfortable spacing by default, or fit more controls with compact density.";

    /// <summary>
    /// Draws the density selector.
    /// </summary>
    /// <param name="setting">
    /// The mutable editor setting value.
    /// </param>
    protected override void OnDraw(EditorSettingObject setting)
    {
        bool compact = setting.GetAsBoolean("compact", false);
        string preview = compact ? "Compact" : "Comfortable";
        NativeImGui.SetNextItemWidth(-1f);
        if (!ImGuiWidget.BeginBoundedCombo("##editor_density", preview))
            return;
        try
        {
            if (NativeImGui.Selectable("Comfortable", !compact))
                setting.SetAsBoolean("compact", false);
            if (NativeImGui.Selectable("Compact", compact))
                setting.SetAsBoolean("compact", true);
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private static EditorSettingObject CreateDefault()
    {
        var result = new EditorSettingObject();
        result.SetAsBoolean("compact", false);
        return result;
    }
}
