using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
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

        int fieldIndex = 0;
        foreach (IGrouping<string, SettingsField> group in page.settings.GroupBy(
                     static setting => setting.section ?? string.Empty,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(group.Key))
                NativeImGui.SeparatorText(group.Key);
            DrawSection(group, ref fieldIndex);
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
        try
        {
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
            try
            {
                bool canReset = session.CanReset(page);
                if (DrawResetButton(canReset))
                    session.Reset(page);
            }
            finally
            {
                NativeImGui.PopID();
            }
        }
        finally
        {
            NativeImGui.EndTable();
        }
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
        IEnumerable<SettingsField> settings,
        ref int fieldIndex)
    {
        ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp |
                                ImGuiTableFlags.NoSavedSettings |
                                ImGuiTableFlags.NoPadOuterX;
        float horizontalBleed = NativeImGui.GetStyle().WindowPadding.X;
        float originalCursorX = NativeImGui.GetCursorPosX();
        float tableWidth = MathF.Max(
            1f,
            NativeImGui.GetContentRegionAvail().X + horizontalBleed * 2f);
        NativeImGui.SetCursorPosX(originalCursorX - horizontalBleed);
        NativeImGui.PushStyleVar(
            ImGuiStyleVar.CellPadding,
            EditorWidget.style.settingsFieldPadding);
        bool tableStarted = false;
        try
        {
            tableStarted = NativeImGui.BeginTable(
                "##settings_section",
                3,
                flags,
                new System.Numerics.Vector2(tableWidth, 0f));
            if (!tableStarted)
                return;

            float labelWidth = Math.Clamp(
                tableWidth * 0.22f,
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
            foreach (SettingsField setting in settings)
                DrawFieldRow(setting, fieldIndex++);
        }
        finally
        {
            if (tableStarted)
                NativeImGui.EndTable();
            NativeImGui.PopStyleVar();
            NativeImGui.SetCursorPosX(originalCursorX);
        }
    }

    private void DrawFieldRow(SettingsField setting, int fieldIndex)
    {
        NativeImGui.TableNextRow();
        uint background = NativeImGui.ColorConvertFloat4ToU32(
            fieldIndex % 2 == 0
                ? EditorPalette.settingsField
                : EditorPalette.settingsFieldAlternate);
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, background);
        NativeImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, background);

        NativeImGui.PushID(setting.path);
        try
        {
            _ = NativeImGui.TableSetColumnIndex(0);
            NativeImGui.SetCursorPosX(
                NativeImGui.GetCursorPosX() + NativeImGui.GetStyle().WindowPadding.X);
            NativeImGui.AlignTextToFramePadding();
            NativeImGui.TextUnformatted(setting.label);
            DrawDescriptionTooltip(setting.description);

            _ = NativeImGui.TableSetColumnIndex(1);
            bool groupStarted = false;
            try
            {
                NativeImGui.BeginGroup();
                groupStarted = true;
                session.UpdateDirty(setting, session.Draw(setting));
                NativeImGui.EndGroup();
                groupStarted = false;

                _ = NativeImGui.TableSetColumnIndex(2);
                bool canReset = session.CanReset(setting);
                if (DrawResetButton(canReset))
                    session.Reset(setting);
            }
            finally
            {
                if (groupStarted)
                    NativeImGui.EndGroup();
            }
        }
        finally
        {
            NativeImGui.PopID();
        }
    }

    private static float GetResetColumnWidth()
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        return style.ItemSpacing.X +
               NativeImGui.CalcTextSize("Reset").X +
               style.FramePadding.X * 2f +
               style.WindowPadding.X;
    }

    private static bool DrawResetButton(bool enabled)
    {
        ImGuiStylePtr style = NativeImGui.GetStyle();
        NativeImGui.SetCursorPosX(NativeImGui.GetCursorPosX() + style.ItemSpacing.X);
        NativeImGui.BeginDisabled(!enabled);
        try
        {
            return NativeImGui.SmallButton("Reset");
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
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
        if (!EditorWidget.BeginMenuTooltip())
            return;
        bool wrapPushed = false;
        try
        {
            NativeImGui.PushTextWrapPos(NativeImGui.GetCursorPosX() + wrapWidth);
            wrapPushed = true;
            NativeImGui.TextUnformatted(description);
        }
        finally
        {
            if (wrapPushed)
                NativeImGui.PopTextWrapPos();
            EditorWidget.EndMenuTooltip();
        }
    }
}
