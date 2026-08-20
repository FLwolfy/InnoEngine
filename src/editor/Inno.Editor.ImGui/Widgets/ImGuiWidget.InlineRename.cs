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
    /// <param name="capacity">The maximum UTF-8 buffer capacity.</param>
    /// <param name="width">The control width, or a negative value to fill the available space.</param>
    /// <returns>The current rename outcome.</returns>
    public static InlineRenameResult InlineRename(
        string id,
        ref string text,
        ref bool requestFocus,
        nuint capacity = 512,
        float width = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (requestFocus)
        {
            NativeImGui.SetKeyboardFocusHere();
            requestFocus = false;
        }

        Vector2 cursor = NativeImGui.GetCursorScreenPos();
        NativeImGui.SetCursorScreenPos(new Vector2(
            cursor.X,
            cursor.Y + style.inlineRenameVerticalInset));
        NativeImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.inlineRenameFramePadding);
        NativeImGui.SetNextItemWidth(width);
        bool submitted;
        try
        {
            submitted = NativeImGui.InputText(
                $"##rename_{id}",
                ref text,
                capacity,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
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

/// <summary>Describes the view-owned geometry used to present an active inline rename action.</summary>
public sealed class InlineRenamePresentation
{
    /// <summary>Creates inline rename presentation data.</summary>
    /// <param name="id">The stable ImGui identifier used by the input field.</param>
    /// <param name="width">The requested input width in logical pixels.</param>
    /// <param name="bufferSize">The maximum UTF-8 buffer size accepted by the input.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty, <paramref name="width"/> is not positive, or <paramref name="bufferSize"/> is zero.</exception>
    public InlineRenamePresentation(string id, float width, nuint bufferSize = 512)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("An inline rename identifier is required.", nameof(id));
        if (width <= 0f)
            throw new ArgumentException("The inline rename width must be positive.", nameof(width));
        if (bufferSize == 0)
            throw new ArgumentException("The inline rename buffer size must be positive.", nameof(bufferSize));
        this.id = id;
        this.width = width;
        this.bufferSize = bufferSize;
    }

    /// <summary>Gets the stable ImGui identifier used by the input field.</summary>
    public string id { get; }

    /// <summary>Gets the requested input width in logical pixels.</summary>
    public float width { get; }

    /// <summary>Gets the maximum UTF-8 buffer size accepted by the input.</summary>
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
