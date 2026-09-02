using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Core.Settings;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Scene;
using Inno.Native.ImGui;
using Inno.Platform.Sdl3.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[ProjectSettingPath("Project/Scene/Tags")]
internal sealed class GameTagsSetting : ProjectSettingEditor<GameTagCatalog>
{
    private const nuint C_TAG_BUFFER_SIZE = 128;

    private string m_error = string.Empty;
    private string m_newTag = string.Empty;

    /// <summary>
    /// Gets the stable project-setting identity used for discovery and persistence.
    /// </summary>
    public override ProjectSettingId settingId => GameTagCatalog.settingId;

    /// <summary>
    /// Gets the presentation section that groups this setting.
    /// </summary>
    public override string section => "Definitions";

    /// <summary>
    /// Gets the user-facing explanation of this feature or setting.
    /// </summary>
    public override string description
        => "Define the project-wide runtime tags that scenes and Plugins may assign to GameObjects.";

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="setting">
    /// The mutable editor setting value currently being presented.
    /// </param>
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
                                ImGuiTableFlags.BordersInnerV |
                                ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoPadOuterX |
                                ImGuiTableFlags.NoSavedSettings;
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, ImGuiWidget.style.cellPadding);
        if (!NativeImGui.BeginTable("##project_tag_definitions", 2, flags))
        {
            NativeImGui.PopStyleVar();
            return;
        }
        try
        {
            NativeImGui.TableSetupColumn(
                "Tag",
                ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoResize);
            NativeImGui.TableSetupColumn(
                "Action",
                ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                72f * ImGuiWidget.style.zoom);
            DrawTagTableHeader();
            for (int i = 0; i < tags.Count; i++)
            {
                string tag = tags[i];
                bool isDefault = string.Equals(tag, GameObject.defaultTag, StringComparison.Ordinal);
                NativeImGui.TableNextRow();
                _ = NativeImGui.TableSetColumnIndex(0);
                NativeImGui.AlignTextToFramePadding();
                InsetPlainCell();
                NativeImGui.TextUnformatted(tag);
                _ = NativeImGui.TableSetColumnIndex(1);
                InsetPlainCell();
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
            NativeImGui.PopStyleVar();
        }
    }

    private static void DrawTagTableHeader()
    {
        uint background = NativeImGui.ColorConvertFloat4ToU32(EditorPalette.collectionHeader);
        NativeImGui.TableNextRow();
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, background);
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, background);
        DrawHeaderCell(0, "Tag");
        DrawHeaderCell(1, "Action");
    }

    private static void DrawHeaderCell(int column, string label)
    {
        _ = NativeImGui.TableSetColumnIndex(column);
        InsetPlainCell();
        NativeImGui.TextUnformatted(label);
    }

    private static void InsetPlainCell()
        => NativeImGui.SetCursorPosX(
            NativeImGui.GetCursorPosX() + NativeImGui.GetStyle().FramePadding.X);
}
