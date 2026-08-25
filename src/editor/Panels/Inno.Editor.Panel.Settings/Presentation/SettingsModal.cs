using System;
using System.Collections.Generic;
using System.Numerics;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Settings;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Settings;

[EditorModal("editor.settings", "Settings", order: 100)]
internal sealed class SettingsModal(
    EditorSettings settings,
    SettingsWindowModule window) : EditorModal
{
    private readonly SettingsTree m_tree = new();

    private string m_query = string.Empty;
    private string m_selectedPath = string.Empty;
    private float m_treePaneRatio = 0.25f;

    /// <inheritdoc />
    public override bool isVisible => window.isVisible;

    /// <inheritdoc />
    public override bool canMove => true;

    /// <inheritdoc />
    public override bool canResize => true;

    /// <inheritdoc />
    public override Vector2 initialSize => new(1050f, 700f);

    /// <inheritdoc />
    public override Vector2 minimumSize => new(760f, 520f);

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        SettingsEditSession? session = window.session;
        if (session is null)
            return;
        if (session.catalogRevision != settings.catalogRevision)
        {
            window.Refresh();
            session = window.session;
            if (session is null)
                return;
        }

        IReadOnlyList<SettingsPage> pages = session.pages;
        SettingsPage? selected = SettingsTree.FindPage(pages, m_selectedPath);
        selected ??= SettingsTree.FindFirstMatch(pages, m_query);
        if (selected is not null)
            m_selectedPath = selected.path;

        float footerHeight = NativeImGui.GetFrameHeightWithSpacing() +
                             NativeImGui.GetStyle().ItemSpacing.Y;
        Vector2 available = NativeImGui.GetContentRegionAvail();
        DrawBody(
            session,
            pages,
            new Vector2(available.X, MathF.Max(1f, available.Y - footerHeight)));
        NativeImGui.Separator();
        DrawButtons(session);
    }

    private void DrawBody(
        SettingsEditSession session,
        IReadOnlyList<SettingsPage> pages,
        Vector2 size)
    {
        ImGuiTableFlags flags = ImGuiTableFlags.NoPadOuterX |
                                ImGuiTableFlags.NoKeepColumnsVisible |
                                ImGuiTableFlags.SizingFixedFit |
                                ImGuiTableFlags.NoSavedSettings;
        float splitterWidth = MathF.Max(
            5f * EditorWidget.style.zoom,
            NativeImGui.GetStyle().DockingSeparatorSize);
        float treeWidth = ResolveTreeWidth(size.X, splitterWidth);
        NativeImGui.PushStyleVar(ImGuiStyleVar.CellPadding, Vector2.Zero);
        bool tableStarted = false;
        try
        {
            tableStarted = NativeImGui.BeginTable("##settings_split", 3, flags, size);
            if (tableStarted)
            {
                NativeImGui.TableSetupColumn(
                    "##settings_tree",
                    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                    treeWidth);
                NativeImGui.TableSetupColumn(
                    "##settings_splitter",
                    ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize,
                    splitterWidth);
                NativeImGui.TableSetupColumn(
                    "##settings_page",
                    ImGuiTableColumnFlags.WidthStretch);
                NativeImGui.TableNextRow();
                _ = NativeImGui.TableSetColumnIndex(0);
                DrawTreePane(pages, size.Y);
                _ = NativeImGui.TableSetColumnIndex(1);
                DrawSplitter(splitterWidth, size.X, treeWidth, size.Y);
                _ = NativeImGui.TableSetColumnIndex(2);
                DrawPagePane(session, pages, size.Y);
            }
        }
        finally
        {
            if (tableStarted)
                NativeImGui.EndTable();
            NativeImGui.PopStyleVar();
        }
    }

    private void DrawTreePane(IReadOnlyList<SettingsPage> pages, float height)
    {
        bool visible = NativeImGui.BeginChild(
            "##settings_tree_pane",
            new Vector2(0f, height),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding);
        try
        {
            if (!visible)
                return;
            bool queryChanged = EditorWidget.SearchInput(
                "settings",
                "Search settings",
                ref m_query,
                width: -1f);
            if (queryChanged)
            {
                SettingsPage? first = SettingsTree.FindFirstMatch(pages, m_query);
                m_selectedPath = first?.path ?? string.Empty;
            }
            NativeImGui.Separator();
            bool scrollVisible = NativeImGui.BeginChild("##settings_tree_scroll", Vector2.Zero);
            try
            {
                if (scrollVisible)
                {
                    m_tree.Draw(
                        pages,
                        m_query,
                        m_selectedPath,
                        page => m_selectedPath = page.path);
                }
            }
            finally
            {
                NativeImGui.EndChild();
            }
        }
        finally
        {
            NativeImGui.EndChild();
        }
    }

    private void DrawPagePane(
        SettingsEditSession session,
        IReadOnlyList<SettingsPage> pages,
        float height)
    {
        bool visible = NativeImGui.BeginChild(
            "##settings_page_pane",
            new Vector2(0f, height),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding);
        try
        {
            if (!visible)
                return;
            SettingsPage? page = SettingsTree.FindPage(pages, m_selectedPath);
            if (page is not null)
            {
                var view = new SettingsPageView(session);
                view.Draw(page, child => m_selectedPath = child.path);
            }
            else
            {
                NativeImGui.TextUnformatted("No matching settings");
            }
        }
        finally
        {
            NativeImGui.EndChild();
        }
    }

    private void DrawSplitter(float width, float availableWidth, float treeWidth, float height)
    {
        _ = NativeImGui.InvisibleButton(
            "##settings_tree_splitter_grip",
            new Vector2(width, MathF.Max(1f, height)));
        bool hovered = NativeImGui.IsItemHovered();
        bool active = NativeImGui.IsItemActive();
        if (hovered || active)
            NativeImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (active)
        {
            Vector2 delta = NativeImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            if (MathF.Abs(delta.X) > 0f)
            {
                float usable = MathF.Max(0f, availableWidth - width);
                float requested = ClampTreeWidth(treeWidth + delta.X, usable);
                m_treePaneRatio = usable > float.Epsilon
                    ? Math.Clamp(requested / usable, 0f, 1f)
                    : 0.25f;
                NativeImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            }
        }

        Vector2 minimum = NativeImGui.GetItemRectMin();
        Vector2 maximum = NativeImGui.GetItemRectMax();
        System.Numerics.Vector4 color = active
            ? EditorPalette.assetAccent
            : hovered
                ? EditorPalette.assetBorder
                : EditorPalette.assetBorderSoft;
        float centerX = (minimum.X + maximum.X) * 0.5f;
        NativeImGui.GetWindowDrawList().AddLine(
            new Vector2(centerX, minimum.Y),
            new Vector2(centerX, maximum.Y),
            NativeImGui.ColorConvertFloat4ToU32(color),
            EditorWidget.style.borderSize);
    }

    private float ResolveTreeWidth(float availableWidth, float splitterWidth)
    {
        float usable = MathF.Max(1f, availableWidth - splitterWidth);
        return ClampTreeWidth(usable * Math.Clamp(m_treePaneRatio, 0f, 1f), usable);
    }

    private static float ClampTreeWidth(float requested, float usable)
    {
        float minimum = MathF.Min(
            EditorWidget.style.assetPaneMinimumVisibleWidth,
            usable * 0.5f);
        float maximum = MathF.Max(minimum, usable - minimum);
        return Math.Clamp(requested, minimum, maximum);
    }

    private void DrawButtons(SettingsEditSession session)
    {
        NativeImGui.BeginDisabled(!session.isDirty);
        try
        {
            if (NativeImGui.Button("Apply"))
                _ = session.Apply();
        }
        finally
        {
            NativeImGui.EndDisabled();
        }

        NativeImGui.SameLine();
        if (NativeImGui.Button("Cancel"))
            window.Close();
    }
}
