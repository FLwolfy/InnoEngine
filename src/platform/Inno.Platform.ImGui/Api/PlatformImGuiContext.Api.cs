using System;

namespace Inno.Platform.ImGui;

/// <summary>
/// Manages Dear ImGui frame lifecycle and rendering integration for a platform window.
/// </summary>
public sealed partial class PlatformImGuiContext : IDisposable
{
    /// <summary>
    /// Registers or replaces a font face for a composable style in this context.
    /// </summary>
    /// <param name="style">The style represented by the font file.</param>
    /// <param name="filePath">Path to a TrueType or OpenType font.</param>
    /// <param name="fontSizePixels">Unscaled font size in pixels.</param>
    /// <exception cref="InvalidOperationException">Thrown after the first ImGui frame has started.</exception>
    public partial void RegisterFontStyle(
        ImGuiFontStyle style,
        string filePath,
        float fontSizePixels = 16f);

    /// <summary>
    /// Sets the ImGui layout file used by this context.
    /// </summary>
    /// <param name="filePath">Absolute or relative layout file path, or <see langword="null"/> to disable persistence.</param>
    /// <exception cref="InvalidOperationException">Thrown after the first ImGui frame has started.</exception>
    public partial void SetIniFile(string? filePath);

    /// <summary>
    /// Renders one ImGui frame by running the provided draw callback.
    /// </summary>
    /// <param name="drawFrame">Draw callback that issues ImGui commands for this frame.</param>
    /// <returns>The native pointer to <c>ImDrawData</c> for the rendered frame.</returns>
    public partial IntPtr RenderFrame(Action drawFrame);

    /// <summary>
    /// Releases all unmanaged/native resources associated with this ImGui context.
    /// </summary>
    public partial void Dispose();
}
