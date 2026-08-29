using System;

using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Global;

[EditorSettingPath("Editor/Appearance/Accessibility/Actual Size")]
internal sealed class ActualSizeSetting : EditorSetting
{
    private static readonly (float Value, string Label)[] C_CHOICES =
    [
        (0.75f, "75%"),
        (0.9f, "90%"),
        (1f, "100%"),
        (1.1f, "110%"),
        (1.25f, "125%"),
        (1.5f, "150%")
    ];

    /// <inheritdoc />
    public override EditorSettingObject defaultValue => CreateDefault();

    /// <inheritdoc />
    public override string section => "Font";

    /// <inheritdoc />
    public override string description
        => "Set the editor's actual font, spacing, control, and window size.";

    /// <inheritdoc />
    protected override void OnDraw(EditorSettingObject setting)
    {
        float value = setting.GetAsSingle("value", 1f);
        string preview = GetLabel(value);
        NativeImGui.SetNextItemWidth(-1f);
        if (!NativeImGui.BeginCombo("##actual_size", preview))
            return;
        try
        {
            for (int i = 0; i < C_CHOICES.Length; i++)
            {
                (float candidate, string label) = C_CHOICES[i];
                if (NativeImGui.Selectable(label, MathF.Abs(candidate - value) < 0.0001f))
                    setting.SetAsSingle("value", candidate);
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private static EditorSettingObject CreateDefault()
    {
        var result = new EditorSettingObject();
        result.SetAsSingle("value", 1f);
        return result;
    }

    private static string GetLabel(float value)
    {
        for (int i = 0; i < C_CHOICES.Length; i++)
        {
            if (MathF.Abs(C_CHOICES[i].Value - value) < 0.0001f)
                return C_CHOICES[i].Label;
        }
        return $"{value * 100f:0}%";
    }
}
