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
    private const float C_INLINE_RENAME_FOCUS_OFFSET = 1f;

    /// <summary>
    /// Draws a compact single-line rename editor inside an existing row.
    /// </summary>
    /// <param name="id">
    /// The stable control identifier.
    /// </param>
    /// <param name="text">
    /// The mutable text buffer.
    /// </param>
    /// <param name="requestFocus">
    /// Whether keyboard focus and complete-value selection should be requested. The value remains
    /// <see langword="true"/> until the input becomes active and its current contents are selected.
    /// </param>
    /// <param name="rowHeight">
    /// The height of the row area in which the field is centered.
    /// </param>
    /// <param name="capacity">
    /// The maximum UTF-8 buffer capacity.
    /// </param>
    /// <param name="width">
    /// The control width, or a negative value to fill the available space.
    /// </param>
    /// <returns>
    /// The current rename outcome.
    /// </returns>
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
        float fieldWidth = MathF.Max(
            1f,
            width < 0f ? NativeImGui.GetContentRegionAvail().X : width);
        string controlId = $"##rename_{id}";
        bool selectAll = requestFocus;
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.inlineRenameFramePadding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, style.frameRounding);
        NativeImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, style.borderSize);
        try
        {
            float fieldHeight = NativeImGui.GetFrameHeight();
            Vector2 cursor = NativeImGui.GetCursorScreenPos();
            NativeImGui.SetCursorScreenPos(new Vector2(
                cursor.X,
                cursor.Y + MathF.Max(0f, (rowHeight - fieldHeight) * 0.5f)));
            NativeImGui.SetNextItemWidth(fieldWidth);
            if (selectAll)
            {
                NativeImGui.SetKeyboardFocusHere();
            }

            bool submitted;
            NativeImGui.PushStyleColor(ImGuiCol.NavCursor, Vector4.Zero);
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
                NativeImGui.PopStyleColor();
            }
            bool deactivated = NativeImGui.IsItemDeactivated();
            bool escapePressed = NativeImGui.IsKeyPressed(ImGuiKey.Escape);
            bool active = NativeImGui.IsItemActive();
            if (selectAll && active)
            {
                uint inputId = NativeImGui.GetItemID();
                ImGuiInputTextStatePtr inputState = ImGuiP.GetInputTextState(inputId);
                if (!inputState.IsNull)
                {
                    ImGuiP.SelectAll(inputState);
                    requestFocus = false;
                }
            }
            if (active)
            {
                Vector2 focusOffset = new(C_INLINE_RENAME_FOCUS_OFFSET);
                NativeImGui.GetForegroundDrawList().AddRect(
                    NativeImGui.GetItemRectMin() - focusOffset,
                    NativeImGui.GetItemRectMax() + focusOffset,
                    NativeImGui.GetColorU32(ImGuiCol.NavCursor),
                    style.frameRounding + C_INLINE_RENAME_FOCUS_OFFSET,
                    ImDrawFlags.RoundCornersAll,
                    style.interactionOverlayThickness);
            }

            if (escapePressed)
                return InlineRenameResult.Cancel;
            if (submitted)
                return InlineRenameResult.Commit;
            return deactivated
                ? InlineRenameResult.FocusLost
                : InlineRenameResult.None;
        }
        finally
        {
            NativeImGui.PopStyleVar(3);
        }
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
    /// <param name="id">
    /// The stable ImGui identifier used by the input field.
    /// </param>
    /// <param name="width">
    /// The requested input width in logical pixels.
    /// </param>
    /// <param name="rowHeight">
    /// The height of the row area in which the field is centered.
    /// </param>
    /// <param name="bufferSize">
    /// The maximum UTF-8 buffer size accepted by the input.
    /// </param>
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

/// <summary>
/// Describes the outcome of an inline rename control.
/// </summary>
public enum InlineRenameResult
{
    /// <summary>
    /// Indicates that the interaction remains active.
    /// </summary>
    None,

    /// <summary>
    /// Indicates that the edited text should be committed.
    /// </summary>
    Commit,

    /// <summary>
    /// Indicates that the input lost focus and its valid value should be committed before closing.
    /// </summary>
    FocusLost,

    /// <summary>
    /// Indicates that the edited text should be discarded.
    /// </summary>
    Cancel
}
