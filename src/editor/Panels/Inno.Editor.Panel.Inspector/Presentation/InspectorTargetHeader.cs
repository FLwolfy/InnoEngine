using System;
using System.Numerics;

using Inno.Editor.Inspection;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using Inno.Platform.ImGui;
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
    /// <param name="selectedTarget">The current global editor selection.</param>
    /// <returns>The current valid Inspector target, or <see langword="null"/> when none is available.</returns>
    internal object? Resolve(object? selectedTarget) => m_lock.Resolve(selectedTarget);

    /// <summary>
    /// Draws the common framed header for a resolved Inspector target.
    /// </summary>
    /// <param name="drawer">The resolved target-specific Inspector drawer.</param>
    /// <param name="context">The drawing context for the current target.</param>
    internal void Draw(IInspectionDrawer drawer, InspectionDrawContext context)
    {
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(context);
        Vector2 origin = NativeImGui.GetCursorScreenPos();
        Vector2 windowPadding = NativeImGui.GetStyle().WindowPadding;
        float left = origin.X - windowPadding.X;
        float right = origin.X + NativeImGui.GetContentRegionAvail().X + windowPadding.X;
        float top = origin.Y - windowPadding.Y;
        NativeImGui.SetCursorScreenPos(new Vector2(left, top));

        NativeImGui.PushStyleColor(ImGuiCol.FrameBg, EditorPalette.inspectorTargetHeader);
        NativeImGui.PushStyleColor(ImGuiCol.Border, EditorPalette.inspectorTargetHeaderBorder);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidget.style.inspectorTargetHeaderPadding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, EditorWidget.style.frameRounding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, EditorWidget.style.borderSize);
        try
        {
            ImGuiChildFlags childFlags = ImGuiChildFlags.FrameStyle | ImGuiChildFlags.AutoResizeY;
            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoScrollbar |
                                           ImGuiWindowFlags.NoScrollWithMouse |
                                           ImGuiWindowFlags.NoSavedSettings;
            if (NativeImGui.BeginChild(
                    "##inspector_target_header",
                    new Vector2(MathF.Max(1f, right - left), 0f),
                    childFlags,
                    windowFlags))
            {
                DrawContent(drawer, context);
            }
            NativeImGui.EndChild();
        }
        finally
        {
            NativeImGui.PopStyleVar(3);
            NativeImGui.PopStyleColor(2);
        }

        float nextY = NativeImGui.GetCursorScreenPos().Y;
        NativeImGui.SetCursorScreenPos(new Vector2(origin.X, nextY));
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
            DrawNameRow(drawer, context, rowHeight);
            NativeImGui.SetCursorPosY(NativeImGui.GetCursorPosY() + rowSpacing);
            DrawCustomRow(drawer, context, rowHeight);
            NativeImGui.EndGroup();
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
        string name = drawer.GetName(context);
        Action<string>? nameSetter = drawer.GetNameSetter(context);
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
            Vector2 textOrigin = NativeImGui.GetCursorScreenPos();
            ImDrawListPtr drawList = NativeImGui.GetWindowDrawList();
            drawList.PushClipRect(
                textOrigin,
                textOrigin + new Vector2(nameWidth, rowHeight),
                true);
            NativeImGui.AlignTextToFramePadding();
            NativeImGui.TextUnformatted(name);
            drawList.PopClipRect();
            NativeImGui.SetCursorScreenPos(
                textOrigin + new Vector2(nameWidth + itemSpacing, 0f));
        }

        string lockIcon = m_lock.isLocked ? ImGuiIcon.Lock : ImGuiIcon.LockOpen;
        string tooltip = m_lock.isLocked ? "Unlock Inspector" : "Lock Inspector";
        if (EditorWidget.ClickableIcon("inspector_target_lock", lockIcon, tooltip))
            m_lock.Toggle(context.target);
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
        if (NativeImGui.BeginChild(
                $"##inspector_target_custom_{GetTargetId(context.target)}",
                new Vector2(0f, rowHeight),
                ImGuiChildFlags.None,
                flags))
        {
            drawer.DrawHeader(context);
        }
        NativeImGui.EndChild();
    }

    private static string GetTargetId(object target)
        => $"{target.GetType().FullName}_{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target)}";
}
