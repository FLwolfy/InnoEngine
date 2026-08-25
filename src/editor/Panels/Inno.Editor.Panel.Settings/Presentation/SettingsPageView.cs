using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Settings;

internal sealed class SettingsPageView(SettingsEditSession session)
{
    internal void Draw(
        SettingsPage page,
        Action<SettingsPage> navigate)
    {
        DrawPageHeader(page);
        NativeImGui.Spacing();
        PushDisabledText();
        EditorWidget.WrappedText(page.description);
        NativeImGui.PopStyleColor();
        NativeImGui.Dummy(new System.Numerics.Vector2(
            0f,
            16f * EditorWidget.style.zoom));

        if (!page.hasSettings)
        {
            DrawOverview(page.children, navigate);
            return;
        }

        foreach (IGrouping<string, EditorSetting> group in page.settings.GroupBy(
                     static setting => setting.section ?? string.Empty,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(group.Key))
                NativeImGui.SeparatorText(group.Key);
            DrawSection(group);
            NativeImGui.Spacing();
        }
    }

    private void DrawPageHeader(SettingsPage page)
    {
        ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoSavedSettings |
                                ImGuiTableFlags.NoPadOuterX;
        if (!NativeImGui.BeginTable("##settings_page_header", 2, flags))
            return;
        NativeImGui.TableSetupColumn(
            "##settings_page_title",
            ImGuiTableColumnFlags.WidthStretch,
            1f);
        NativeImGui.TableSetupColumn(
            "##settings_page_reset",
            ImGuiTableColumnFlags.WidthFixed,
            GetResetColumnWidth());
        NativeImGui.TableNextRow();
        _ = NativeImGui.TableSetColumnIndex(0);
        NativeImGui.AlignTextToFramePadding();
        NativeImGui.TextUnformatted(page.label);
        _ = NativeImGui.TableSetColumnIndex(1);
        NativeImGui.PushID(page.path);
        bool canReset = session.CanReset(page);
        if (DrawResetButton(canReset))
            session.Reset(page);
        NativeImGui.PopID();
        NativeImGui.EndTable();
    }

    private static void DrawOverview(
        IReadOnlyList<SettingsPage> children,
        Action<SettingsPage> navigate)
    {
        if (children.Count == 0)
        {
            PushDisabledText();
            NativeImGui.TextUnformatted("No settings are currently registered on this page.");
            NativeImGui.PopStyleColor();
            return;
        }

        for (int i = 0; i < children.Count; i++)
        {
            SettingsPage child = children[i];
            bool clicked = EditorWidget.HoverText(
                $"settings_overview_{child.path}",
                child.label);
            if (clicked)
                navigate(child);
            DrawDescriptionTooltip(child.description);
            NativeImGui.Spacing();
        }
    }

    private void DrawSection(
        IEnumerable<EditorSetting> settings)
    {
        ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoSavedSettings |
                                ImGuiTableFlags.NoPadOuterX;
        if (!NativeImGui.BeginTable("##settings_section", 3, flags))
            return;
        float available = MathF.Max(1f, NativeImGui.GetContentRegionAvail().X);
        float labelWidth = Math.Clamp(
            available * 0.22f,
            120f * EditorWidget.style.zoom,
            240f * EditorWidget.style.zoom);
        NativeImGui.TableSetupColumn(
            "##settings_label",
            ImGuiTableColumnFlags.WidthFixed,
            labelWidth);
        NativeImGui.TableSetupColumn(
            "##settings_content",
            ImGuiTableColumnFlags.WidthStretch,
            1f);
        NativeImGui.TableSetupColumn(
            "##settings_reset",
            ImGuiTableColumnFlags.WidthFixed,
            GetResetColumnWidth());
        foreach (EditorSetting setting in settings)
        {
            NativeImGui.TableNextRow();
            _ = NativeImGui.TableSetColumnIndex(0);
            NativeImGui.AlignTextToFramePadding();
            NativeImGui.TextUnformatted(setting.label);
            DrawDescriptionTooltip(setting.description);

            _ = NativeImGui.TableSetColumnIndex(1);
            NativeImGui.PushID(setting.path);
            NativeImGui.BeginGroup();
            session.UpdateDirty(setting, setting.Draw(session.Get(setting)));
            NativeImGui.EndGroup();

            _ = NativeImGui.TableSetColumnIndex(2);
            bool canReset = session.CanReset(setting);
            if (DrawResetButton(canReset))
                session.Reset(setting);
            NativeImGui.PopID();
        }
        NativeImGui.EndTable();
    }

    private static float GetResetColumnWidth()
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        return style.ItemSpacing.X +
               NativeImGui.CalcTextSize("Reset").X +
               style.FramePadding.X * 2f;
    }

    private static bool DrawResetButton(bool enabled)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        NativeImGui.SetCursorPosX(NativeImGui.GetCursorPosX() + style.ItemSpacing.X);
        NativeImGui.BeginDisabled(!enabled);
        bool clicked = NativeImGui.SmallButton("Reset");
        NativeImGui.EndDisabled();
        return clicked;
    }

    private static void PushDisabledText()
        => NativeImGui.PushStyleColor(
            ImGuiCol.Text,
            NativeImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);

    private static void DrawDescriptionTooltip(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || !NativeImGui.IsItemHovered())
            return;

        float minimumWidth = 300f * EditorWidget.style.zoom;
        float wrapWidth = 440f * EditorWidget.style.zoom;
        NativeImGui.SetNextWindowSizeConstraints(
            new System.Numerics.Vector2(minimumWidth, 0f),
            new System.Numerics.Vector2(wrapWidth, float.MaxValue));
        if (!NativeImGui.BeginTooltip())
            return;
        NativeImGui.PushTextWrapPos(NativeImGui.GetCursorPosX() + wrapWidth);
        NativeImGui.TextUnformatted(description);
        NativeImGui.PopTextWrapPos();
        NativeImGui.EndTooltip();
    }
}
