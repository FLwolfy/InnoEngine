using System;
using System.Numerics;

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
    /// Loads Dear ImGui layout settings from an application-owned text document.
    /// </summary>
    /// <param name="settings">The Dear ImGui layout text, or an empty string when no layout exists.</param>
    /// <exception cref="InvalidOperationException">Thrown after the first ImGui frame has started.</exception>
    public partial void LoadIniSettings(string? settings);

    /// <summary>
    /// Captures Dear ImGui layout settings when the layout is dirty or when capture is explicitly forced.
    /// </summary>
    /// <param name="settings">The complete Dear ImGui layout text when capture succeeds.</param>
    /// <param name="force">Whether to capture even when Dear ImGui did not request persistence.</param>
    /// <returns><see langword="true"/> when layout text was captured.</returns>
    public partial bool TryCaptureIniSettings(out string settings, bool force = false);

    /// <summary>
    /// Renders one ImGui frame by running the provided draw callback.
    /// </summary>
    /// <param name="drawFrame">Draw callback that issues ImGui commands for this frame.</param>
    /// <returns>The native pointer to <c>ImDrawData</c> for the rendered frame.</returns>
    public partial IntPtr RenderFrame(Action drawFrame);

    /// <summary>Draws a renderer-registered texture without exposing a native ImGui texture identifier.</summary>
    /// <param name="texture">Opaque token allocated by the active presentation backend.</param>
    /// <param name="size">Displayed size in logical ImGui pixels.</param>
    /// <param name="uv0">Top-left texture coordinate.</param>
    /// <param name="uv1">Bottom-right texture coordinate.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="texture"/> is invalid.</exception>
    public unsafe partial void DrawImage(
        ImGuiTextureHandle texture,
        Vector2 size,
        Vector2 uv0 = default,
        Vector2 uv1 = default);

    /// <summary>
    /// Releases all unmanaged/native resources associated with this ImGui context.
    /// </summary>
    public partial void Dispose();
}
