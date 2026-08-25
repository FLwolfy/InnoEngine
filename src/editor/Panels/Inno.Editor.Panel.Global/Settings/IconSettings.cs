using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Global;

internal abstract class IconSetting : EditorSetting
{
    private static readonly IReadOnlyList<(string Name, string Glyph)> C_ICONS =
        typeof(ImGuiIcon)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(static field => (field.Name, (string)field.GetRawConstantValue()!))
            .OrderBy(static value => value.Name, StringComparer.Ordinal)
            .ToArray();

    private static int s_iconSlotFrame = -1;
    private static float s_iconSlotWidth;

    private readonly string m_glyph;

    protected IconSetting(string glyph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glyph);
        m_glyph = glyph;
    }

    public override EditorSettingObject defaultValue => CreateDefault(m_glyph);

    public override string section => "Editor Icons";

    protected override void OnDraw(EditorSettingObject setting)
    {
        string value = setting.GetAsString("value", m_glyph) ?? m_glyph;
        float iconSlotWidth = GetIconSlotWidth();
        ImDrawListPtr comboDrawList = NativeImGui.GetWindowDrawList();
        NativeImGui.SetNextItemWidth(-1f);
        bool isOpen = NativeImGui.BeginCombo("##icon", string.Empty);
        Vector2 comboMinimum = NativeImGui.GetItemRectMin();
        Vector2 comboMaximum = NativeImGui.GetItemRectMax();
        ImGuiStylePtr comboStyle = NativeImGui.GetStyle();
        float comboLineHeight = NativeImGui.GetTextLineHeight();
        DrawIconTextAt(
            comboDrawList,
            new Vector2(
                comboMinimum.X + comboStyle.FramePadding.X,
                comboMinimum.Y + MathF.Max(
                    0f,
                    (comboMaximum.Y - comboMinimum.Y - comboLineHeight) * 0.5f)),
            value,
            GetIconName(value),
            iconSlotWidth);
        if (isOpen)
        {
            try
            {
                for (int i = 0; i < C_ICONS.Count; i++)
                {
                    (string name, string candidate) = C_ICONS[i];
                    NativeImGui.PushID(name);
                    try
                    {
                        bool selected = string.Equals(candidate, value, StringComparison.Ordinal);
                        if (NativeImGui.Selectable(
                                "##icon_option",
                                selected,
                                ImGuiSelectableFlags.None,
                                new Vector2(0f, NativeImGui.GetFrameHeight())))
                        {
                            value = candidate;
                            setting.SetAsString("value", candidate);
                        }
                        Vector2 minimum = NativeImGui.GetItemRectMin();
                        Vector2 maximum = NativeImGui.GetItemRectMax();
                        ImGuiStylePtr style = NativeImGui.GetStyle();
                        float lineHeight = NativeImGui.GetTextLineHeight();
                        DrawIconTextAt(
                            NativeImGui.GetWindowDrawList(),
                            new Vector2(
                                minimum.X + style.FramePadding.X,
                                minimum.Y + MathF.Max(0f, (maximum.Y - minimum.Y - lineHeight) * 0.5f)),
                            candidate,
                            name,
                            iconSlotWidth);
                        if (selected)
                            NativeImGui.SetItemDefaultFocus();
                    }
                    finally
                    {
                        NativeImGui.PopID();
                    }
                }
            }
            finally
            {
                NativeImGui.EndCombo();
            }
        }
    }

    private static EditorSettingObject CreateDefault(string glyph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glyph);
        var result = new EditorSettingObject();
        result.SetAsString("value", glyph);
        return result;
    }

    private static float GetIconSlotWidth()
    {
        int frame = NativeImGui.GetFrameCount();
        if (s_iconSlotFrame == frame)
            return s_iconSlotWidth;

        ImFontPtr font = NativeImGui.GetFont();
        float fontSize = NativeImGui.GetFontSize();
        float result = NativeImGui.GetTextLineHeight();
        for (int i = 0; i < C_ICONS.Count; i++)
        {
            Vector4 bounds = ImGuiWidget.GetGlyphVisualBounds(font, fontSize, C_ICONS[i].Glyph);
            result = MathF.Max(result, bounds.Z - bounds.X);
        }
        s_iconSlotFrame = frame;
        s_iconSlotWidth = result;
        return s_iconSlotWidth;
    }

    private static string GetIconName(string glyph)
    {
        for (int i = 0; i < C_ICONS.Count; i++)
        {
            if (string.Equals(C_ICONS[i].Glyph, glyph, StringComparison.Ordinal))
                return C_ICONS[i].Name;
        }
        return glyph;
    }

    private static void DrawIconTextAt(
        ImDrawListPtr drawList,
        Vector2 origin,
        string glyph,
        string text,
        float iconSlotWidth)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        ImFontPtr font = NativeImGui.GetFont();
        float fontSize = NativeImGui.GetFontSize();
        float lineHeight = NativeImGui.GetTextLineHeight();
        uint color = NativeImGui.GetColorU32(ImGuiCol.Text);
        ImGuiWidget.AddGlyphCentered(
            drawList,
            font,
            fontSize,
            glyph,
            new Vector2(origin.X + iconSlotWidth * 0.5f, origin.Y + lineHeight * 0.5f),
            color);
        drawList.AddText(
            new Vector2(origin.X + iconSlotWidth + style.ItemInnerSpacing.X, origin.Y),
            color,
            text);
    }
}

[EditorSettingPath("Global/Appearance/Icons/Scene")]
internal sealed class SceneIconSetting() : IconSetting(ImGuiIcon.Cubes)
{
    public override string description
        => "Selects the icon used wherever the editor presents a scene document or loaded scene.";
}

[EditorSettingPath("Global/Appearance/Icons/GameObject")]
internal sealed class GameObjectIconSetting() : IconSetting(ImGuiIcon.Cube)
{
    public override string description
        => "Selects the icon used wherever the editor presents a scene GameObject.";
}

[EditorSettingPath("Global/Appearance/Icons/Prefab")]
internal sealed class PrefabIconSetting() : IconSetting(ImGuiIcon.Cube)
{
    public override string description
        => "Selects the icon used wherever the editor presents a reusable prefab object.";
}

[EditorSettingPath("Global/Appearance/Icons/Layers")]
internal sealed class LayersIconSetting() : IconSetting(ImGuiIcon.LayerGroup)
{
    public override string description
        => "Selects the icon used wherever the editor presents a project layer configuration.";
}

[EditorSettingPath("Global/Appearance/Icons/Folder")]
internal sealed class FolderIconSetting() : IconSetting(ImGuiIcon.Folder)
{
    public override string description
        => "Selects the icon used wherever the editor presents a source directory.";
}

[EditorSettingPath("Global/Appearance/Icons/File")]
internal sealed class FileIconSetting() : IconSetting(ImGuiIcon.File)
{
    public override string description
        => "Selects the icon used wherever the editor presents a generic source file.";
}
