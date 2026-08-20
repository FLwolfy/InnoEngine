using System;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.Widgets;

/// <summary>
/// Provides reusable editor controls and rendering helpers built on the native ImGui API.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Draws a single-line search field with a stable identifier.
    /// </summary>
    /// <param name="id">The stable control identifier.</param>
    /// <param name="hint">The hint shown while the query is empty.</param>
    /// <param name="query">The mutable search query.</param>
    /// <param name="capacity">The maximum UTF-8 buffer capacity.</param>
    /// <param name="width">The control width, or a negative value to fill the available space.</param>
    /// <returns><see langword="true"/> when the query changed; otherwise, <see langword="false"/>.</returns>
    public static bool SearchInput(
        string id,
        string hint,
        ref string query,
        nuint capacity = 256,
        float width = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        NativeImGui.SetNextItemWidth(width);
        return NativeImGui.InputTextWithHint($"##search_{id}", hint, ref query, capacity);
    }

    /// <summary>
    /// Begins a popup and draws its focused search field.
    /// </summary>
    /// <param name="id">The popup identifier previously supplied to ImGui.</param>
    /// <param name="query">The mutable search query.</param>
    /// <param name="hint">The hint shown while the query is empty.</param>
    /// <param name="capacity">The maximum UTF-8 buffer capacity.</param>
    /// <param name="width">The search field width, or a negative value to use the editor default.</param>
    /// <returns><see langword="true"/> when popup content should be drawn; otherwise, <see langword="false"/>.</returns>
    public static bool BeginSearchPopup(
        string id,
        ref string query,
        string hint,
        nuint capacity = 256,
        float width = -1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (width < 0f)
            width = style.searchPopupWidth;
        if (!NativeImGui.BeginPopup(id))
            return false;
        if (NativeImGui.IsWindowAppearing())
            NativeImGui.SetKeyboardFocusHere();
        _ = SearchInput(id, hint, ref query, capacity, width);
        NativeImGui.Separator();
        return true;
    }

    /// <summary>
    /// Ends a popup opened by <see cref="BeginSearchPopup"/>.
    /// </summary>
    public static void EndSearchPopup() => NativeImGui.EndPopup();
}
