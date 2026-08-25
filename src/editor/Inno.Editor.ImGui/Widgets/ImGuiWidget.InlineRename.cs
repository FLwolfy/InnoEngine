using System;
using System.Numerics;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a compact single-line rename editor inside an existing row.
    /// </summary>
    /// <param name="id">The stable control identifier.</param>
    /// <param name="text">The mutable text buffer.</param>
    /// <param name="requestFocus">Whether keyboard focus should be requested during this frame.</param>
    /// <param name="rowHeight">The height of the row area in which the field is centered.</param>
    /// <param name="capacity">The maximum UTF-8 buffer capacity.</param>
    /// <param name="width">The control width, or a negative value to fill the available space.</param>
    /// <returns>The current rename outcome.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="rowHeight"/> is not finite or is not positive.
    /// </exception>
    public static InlineRenameResult InlineRename(
        string id,
        ref string text,
        ref bool requestFocus,
        float rowHeight,
        nuint capacity = 512,
        float width = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!float.IsFinite(rowHeight) || rowHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowHeight),
                rowHeight,
                "Inline rename row height must be finite and positive.");
        }
        if (requestFocus)
        {
            NativeImGui.SetKeyboardFocusHere();
            requestFocus = false;
        }

        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.inlineRenameFramePadding);
        NativeImGui.SetNextItemWidth(width);
        bool submitted;
        try
        {
            float fieldHeight = NativeImGui.GetFrameHeight();
            Vector2 fieldCursor = new(
                cursor.X,
                cursor.Y + (rowHeight - fieldHeight) * 0.5f);
            NativeImGui.SetCursorScreenPos(fieldCursor);
            float fieldWidth = MathF.Max(1f, width < 0f ? NativeImGui.CalcItemWidth() : width);
            float visualHeight = MathF.Max(
                1f,
                MathF.Min(
                    fieldHeight,
                    rowHeight - style.inlineRenameVisualInset * 2f));
            Vector2 visualMinimum = new(
                fieldCursor.X,
                cursor.Y + (rowHeight - visualHeight) * 0.5f);
            Vector2 visualMaximum = visualMinimum + new Vector2(fieldWidth, visualHeight);
            string controlId = $"##rename_{id}";
            uint itemId = NativeImGui.GetID(controlId);
            bool active = ImGuiP.GetActiveID() == itemId;
            bool hovered = NativeImGui.IsMouseHoveringRect(
                fieldCursor,
                fieldCursor + new Vector2(fieldWidth, fieldHeight));
            ImGuiCol backgroundColor = active
                ? ImGuiCol.FrameBgActive
                : hovered
                    ? ImGuiCol.FrameBgHovered
                    : ImGuiCol.FrameBg;
            NativeImGui.GetWindowDrawList().AddRectFilled(
                visualMinimum,
                visualMaximum,
                NativeImGui.GetColorU32(backgroundColor),
                NativeImGui.GetStyle().FrameRounding);
            NativeImGui.PushStyleColor(ImGuiCol.FrameBg, EditorPalette.transparent);
            NativeImGui.PushStyleColor(ImGuiCol.FrameBgHovered, EditorPalette.transparent);
            NativeImGui.PushStyleColor(ImGuiCol.FrameBgActive, EditorPalette.transparent);
            try
            {
                submitted = NativeImGui.InputText(
                    controlId,
                    ref text,
                    capacity,
                    ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            }
            finally
            {
                NativeImGui.PopStyleColor(3);
            }
        }
        finally
        {
            NativeImGui.PopStyleVar();
        }
        if (NativeImGui.IsKeyPressed(ImGuiKey.Escape))
            return InlineRenameResult.Cancel;
        if (submitted)
            return InlineRenameResult.Commit;
        return NativeImGui.IsItemDeactivated()
            ? InlineRenameResult.FocusLost
            : InlineRenameResult.None;
    }
}

/// <summary>
/// Describes the view-owned geometry used to present an active inline rename action.
/// </summary>
public sealed class InlineRenamePresentation
{
    /// <summary>
    /// Creates inline rename presentation data.
    /// </summary>
    /// <param name="id">The stable ImGui identifier used by the input field.</param>
    /// <param name="width">The requested input width in logical pixels.</param>
    /// <param name="rowHeight">The height of the row area in which the field is centered.</param>
    /// <param name="bufferSize">The maximum UTF-8 buffer size accepted by the input.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="id"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="width"/> or <paramref name="rowHeight"/> is not finite and
    /// positive, or when <paramref name="bufferSize"/> is zero.
    /// </exception>
    public InlineRenamePresentation(
        string id,
        float width,
        float rowHeight,
        nuint bufferSize = 512)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An inline rename identifier is required.", nameof(id));
        if (!float.IsFinite(width) || width <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The inline rename width must be finite and positive.");
        }
        if (!float.IsFinite(rowHeight) || rowHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowHeight),
                rowHeight,
                "The inline rename row height must be finite and positive.");
        }
        if (bufferSize == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bufferSize),
                bufferSize,
                "The inline rename buffer size must be positive.");
        }
        this.id = id;
        this.width = width;
        this.rowHeight = rowHeight;
        this.bufferSize = bufferSize;
    }

    /// <summary>
    /// Gets the stable ImGui identifier used by the input field.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the requested input width in logical pixels.
    /// </summary>
    public float width { get; }

    /// <summary>
    /// Gets the height of the row area in which the input field is centered.
    /// </summary>
    public float rowHeight { get; }

    /// <summary>
    /// Gets the maximum UTF-8 buffer size accepted by the input.
    /// </summary>
    public nuint bufferSize { get; }
}

/// <summary>Describes the outcome of an inline rename control.</summary>
public enum InlineRenameResult
{
    /// <summary>Indicates that the interaction remains active.</summary>
    None,

    /// <summary>Indicates that the edited text should be committed.</summary>
    Commit,

    /// <summary>Indicates that the input lost focus and its valid value should be committed before closing.</summary>
    FocusLost,

    /// <summary>Indicates that the edited text should be discarded.</summary>
    Cancel
}
