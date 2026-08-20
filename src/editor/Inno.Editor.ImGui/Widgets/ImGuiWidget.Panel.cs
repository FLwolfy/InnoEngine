using System;

using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.ImGui.ImGuiWidget;

/// <summary>
/// Reusable editor widgets built on top of <see cref="ImGui"/>.
/// </summary>
public static partial class ImGuiWidget
{
    /// <summary>
    /// Opens a standard panel window and executes panel body.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="isOpen">Visible state.</param>
    /// <param name="drawBody">Panel body callback.</param>
    /// <param name="flags">Window flags.</param>
    public static void PanelWindow(
        string title,
        ref bool isOpen,
        Action drawBody,
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse)
    {
        if (!isOpen)
            return;

        // Avoid p_open overload to keep native interop surface minimal and stable.
        if (NativeImGui.Begin(title, flags))
        {
            drawBody();
        }

        NativeImGui.End();
    }

    /// <summary>
    /// Draws a disabled hint text line.
    /// </summary>
    /// <param name="text">Hint text.</param>
    public static void Hint(string text)
    {
        NativeImGui.BeginDisabled(true);
        NativeImGui.TextUnformatted(text);
        NativeImGui.EndDisabled();
    }
}
