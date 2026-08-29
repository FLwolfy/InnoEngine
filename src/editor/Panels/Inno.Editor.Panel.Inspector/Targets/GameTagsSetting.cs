using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Settings;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Engine.Scene;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[ProjectSettingPath("Project/Scene/Tags")]
internal sealed class GameTagsSetting : ProjectSettingEditor<GameTagCatalog>
{
    private const nuint C_TAG_BUFFER_SIZE = 128;

    private string m_error = string.Empty;
    private string m_newTag = string.Empty;

    /// <inheritdoc />
    public override ProjectSettingId settingId => GameTagCatalog.settingId;

    /// <inheritdoc />
    public override string section => "Definitions";

    /// <inheritdoc />
    public override string description
        => "Define the project-wide runtime tags that scenes and Plugins may assign to GameObjects.";

    /// <inheritdoc />
    protected override void OnDraw(GameTagCatalog setting)
    {
        DrawAddRow(setting);
        NativeImGui.Spacing();
        DrawTags(setting);
        if (!string.IsNullOrEmpty(m_error))
            ImGuiWidget.ColoredText(EditorPalette.error, m_error);
    }

    private void DrawAddRow(GameTagCatalog setting)
    {
        float spacing = NativeImGui.GetStyle().ItemSpacing.X;
        float actionWidth = EditorWidget.GetCompactIconSize().X;
        NativeImGui.SetNextItemWidth(MathF.Max(
            1f,
            NativeImGui.GetContentRegionAvail().X - actionWidth - spacing));
        bool submit = NativeImGui.InputTextWithHint(
            "##new_project_tag",
            "New tag",
            ref m_newTag,
            C_TAG_BUFFER_SIZE,
            ImGuiInputTextFlags.EnterReturnsTrue);
        NativeImGui.SameLine(0f, spacing);
        submit |= EditorWidget.ClickableIcon(
            "add_project_tag",
            ImGuiIcon.Plus,
            "Add project tag");
        if (!submit || string.IsNullOrWhiteSpace(m_newTag))
            return;

        try
        {
            if (!setting.Add(m_newTag))
                m_error = $"Tag '{m_newTag.Trim()}' is already defined.";
            else
                m_error = string.Empty;
            m_newTag = string.Empty;
        }
        catch (ArgumentException exception)
        {
            m_error = exception.Message;
        }
    }

    private static void DrawTags(GameTagCatalog setting)
    {
        IReadOnlyList<string> tags = setting.GetTags();
        ImGuiTableFlags flags = ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.BordersInnerH |
                                ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoPadOuterX |
                                ImGuiTableFlags.NoSavedSettings;
        if (!NativeImGui.BeginTable("##project_tag_definitions", 2, flags))
            return;
        try
        {
            NativeImGui.TableSetupColumn("Tag", ImGuiTableColumnFlags.WidthStretch);
            NativeImGui.TableSetupColumn(
                "Action",
                ImGuiTableColumnFlags.WidthFixed,
                72f * ImGuiWidget.style.zoom);
            NativeImGui.TableHeadersRow();
            for (int i = 0; i < tags.Count; i++)
            {
                string tag = tags[i];
                bool isDefault = string.Equals(tag, GameObject.defaultTag, StringComparison.Ordinal);
                NativeImGui.TableNextRow();
                _ = NativeImGui.TableSetColumnIndex(0);
                NativeImGui.AlignTextToFramePadding();
                NativeImGui.TextUnformatted(tag);
                _ = NativeImGui.TableSetColumnIndex(1);
                if (isDefault)
                {
                    ImGuiWidget.ColoredText(EditorPalette.textDisabled, "Fixed");
                    continue;
                }
                if (EditorWidget.ClickableText(
                        $"remove_project_tag_{tag}",
                        "Remove",
                        new Vector2(
                            NativeImGui.CalcTextSize("Remove").X,
                            NativeImGui.GetFrameHeight()),
                        "Remove this definition. Existing scene assignments are preserved and become undefined."))
                {
                    _ = setting.Remove(tag);
                }
            }
        }
        finally
        {
            NativeImGui.EndTable();
        }
    }
}
