using System;
using System.Numerics;

using Inno.Editor.Inspection;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Interactions;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using Inno.Adapter.Presentation.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Draws the persistent identity header shared by every Inspector target type.
/// </summary>
internal sealed class InspectorTargetHeader
{
    private const nuint C_NAME_BUFFER_SIZE = 512;

    private readonly InspectorLockControl m_lock = new();

    /// <summary>
    /// Resolves the target that should be presented after applying Inspector lock state.
    /// </summary>
    /// <returns>
    /// The current valid Inspector target, or <see langword="null"/> when none is available.
    /// </returns>
    /// <param name="interactions">
    /// Shared interaction service that resolves targets in their owning identity domains.
    /// </param>
    /// <param name="selectedTarget">
    /// The current inspection target used to render the header.
    /// </param>
    internal object? Resolve(EditorInteractions interactions, object? selectedTarget)
        => m_lock.Resolve(interactions, selectedTarget);

    /// <summary>
    /// Draws the common framed header for a resolved Inspector target.
    /// </summary>
    /// <param name="drawer">
    /// The resolved target-specific Inspector drawer.
    /// </param>
    /// <param name="context">
    /// The drawing context for the current target.
    /// </param>
    internal void Draw(IInspectionDrawer drawer, InspectionDrawContext context)
    {
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(context);
        EditorWidget.HeaderSurface(
            "##inspector_target_header",
            () => DrawContent(drawer, context),
            spanWindowPadding: true);
    }

    private void DrawContent(IInspectionDrawer drawer, InspectionDrawContext context)
    {
        NativeImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, EditorWidget.style.compactItemSpacing);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.compactFramePadding);
        try
        {
            float rowHeight = NativeImGui.GetFrameHeight();
            float rowSpacing = EditorWidget.style.inspectorTargetHeaderRowSpacing;
            float headerContentHeight = rowHeight * 2f + rowSpacing;
            DrawIcon(drawer.GetIcon(context), headerContentHeight);
            NativeImGui.SameLine();

            NativeImGui.BeginGroup();
            try
            {
                DrawNameRow(drawer, context, rowHeight);
                NativeImGui.SetCursorPosY(NativeImGui.GetCursorPosY() + rowSpacing);
                DrawCustomRow(drawer, context, rowHeight);
            }
            finally
            {
                NativeImGui.EndGroup();
            }
        }
        finally
        {
            NativeImGui.PopStyleVar(2);
        }
    }

    private void DrawNameRow(
        IInspectionDrawer drawer,
        InspectionDrawContext context,
        float rowHeight)
    {
        Vector2 lockSize = EditorWidget.GetCompactIconSize();
        float itemSpacing = NativeImGui.GetStyle().ItemSpacing.X;
        float nameWidth = MathF.Max(
            1f,
            NativeImGui.GetContentRegionAvail().X - lockSize.X - itemSpacing);
        (string name, Action<string>? nameSetter) = drawer.BindName(context);
        if (nameSetter is not null)
        {
            NativeImGui.SetNextItemWidth(nameWidth);
            if (NativeImGui.InputText(
                    $"##inspector_target_name_{GetTargetId(context.target)}",
                    ref name,
                    C_NAME_BUFFER_SIZE,
                    ImGuiInputTextFlags.None))
            {
                nameSetter(name);
            }
            NativeImGui.SameLine(0f, itemSpacing);
        }
        else
        {
            NativeImGui.AlignTextToFramePadding();
            Vector2 textOrigin = NativeImGui.GetCursorScreenPos();
            ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
            drawList.PushClipRect(
                textOrigin,
                textOrigin + new Vector2(nameWidth, rowHeight),
                true);
            try
            {
                drawList.AddText(textOrigin, NativeImGui.GetColorU32(ImGuiCol.Text), name);
            }
            finally
            {
                drawList.PopClipRect();
            }
            NativeImGui.Dummy(new Vector2(nameWidth, rowHeight));
            NativeImGui.SameLine(0f, itemSpacing);
        }

        string lockIcon = m_lock.isLocked ? ImGuiIcon.Lock : ImGuiIcon.LockOpen;
        string tooltip = m_lock.isLocked ? "Unlock Inspector" : "Lock Inspector";
        if (EditorWidget.ClickableIcon("inspector_target_lock", lockIcon, tooltip))
            m_lock.Toggle(context.interactions, context.target);
    }

    private static void DrawIcon(string icon, float slotSize)
    {
        Vector2 minimum = NativeImGui.GetCursorScreenPos();
        EditorWidget.AddGlyphCentered(
            NativeImGui.GetWindowDrawList(),
            NativeImGui.GetFont(),
            NativeImGui.GetFontSize() * EditorWidget.style.inspectorTargetIconScale,
            icon,
            minimum + new Vector2(slotSize * 0.5f),
            NativeImGui.GetColorU32(ImGuiCol.Text));
        NativeImGui.Dummy(new Vector2(slotSize));
    }

    private static void DrawCustomRow(
        IInspectionDrawer drawer,
        InspectionDrawContext context,
        float rowHeight)
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar |
                                 ImGuiWindowFlags.NoScrollWithMouse |
                                 ImGuiWindowFlags.NoSavedSettings;
        bool visible = NativeImGui.BeginChild(
            $"##inspector_target_custom_{GetTargetId(context.target)}",
            new Vector2(0f, rowHeight),
            ImGuiChildFlags.None,
            flags);
        try
        {
            if (visible)
                drawer.DrawHeader(context);
        }
        finally
        {
            NativeImGui.EndChild();
        }
    }

    private static string GetTargetId(object target)
        => $"{target.GetType().FullName}_{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target)}";
}
