using System;
using Inno.Platform;

namespace Inno.Platform.Sdl3.ImGui;

/// <summary>
/// Identifies a backend-owned texture through an opaque ImGui token instead of a native graphics handle.
/// </summary>
public readonly record struct ImGuiTextureHandle
{
    /// <summary>
    /// Creates an opaque texture token allocated by an ImGui presentation backend.
    /// </summary>
    /// <param name="value">
    /// Non-zero backend token.
    /// </param>
    public ImGuiTextureHandle(ulong value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        this.value = value;
    }

    /// <summary>
    /// Gets the opaque token consumed only by the active ImGui backend.
    /// </summary>
    public ulong value { get; }

    /// <summary>
    /// Gets whether this token identifies a registered texture.
    /// </summary>
    public bool isValid => value != 0;
}

/// <summary>
/// Describes one detached ImGui viewport without exposing SDL or graphics-backend handles.
/// </summary>
public sealed class PlatformImGuiViewportTarget
{
    internal PlatformImGuiViewportTarget(
        uint viewportId,
        uint windowId,
        PlatformNativeHandles nativeHandles,
        int width,
        int height)
    {
        this.viewportId = viewportId;
        this.windowId = windowId;
        this.nativeHandles = nativeHandles;
        this.width = width;
        this.height = height;
    }

    /// <summary>
    /// Gets the stable Dear ImGui viewport identity.
    /// </summary>
    public uint viewportId { get; }

    /// <summary>
    /// Gets the platform window identity.
    /// </summary>
    public uint windowId { get; }

    /// <summary>
    /// Gets backend-neutral native-window integration handles.
    /// </summary>
    public PlatformNativeHandles nativeHandles { get; }

    /// <summary>
    /// Gets the current framebuffer width.
    /// </summary>
    public int width { get; internal set; }

    /// <summary>
    /// Gets the current framebuffer height.
    /// </summary>
    public int height { get; internal set; }
}

/// <summary>
/// Presents ImGui draw data through a replaceable renderer while platform input and windows stay shared.
/// </summary>
public interface IPlatformImGuiRenderer : IDisposable
{
    /// <summary>
    /// Gets whether detached viewport windows are rendered by this backend.
    /// </summary>
    bool supportsViewports { get; }

    /// <summary>
    /// Consumes current main-window draw data before the next ImGui frame begins.
    /// </summary>
    /// <param name="drawData">
    /// Opaque native ImDrawData pointer valid for the current frame.
    /// </param>
    void RenderMain(IntPtr drawData);

    /// <summary>
    /// Synchronizes the primary drawable after a platform resize.
    /// </summary>
    /// <param name="pixelWidth">
    /// Current drawable width in physical pixels.
    /// </param>
    /// <param name="pixelHeight">
    /// Current drawable height in physical pixels.
    /// </param>
    void SynchronizeMainOutput(int pixelWidth, int pixelHeight);

    /// <summary>
    /// Creates backend presentation state for one detached viewport.
    /// </summary>
    /// <param name="target">
    /// New viewport target.
    /// </param>
    void CreateViewport(PlatformImGuiViewportTarget target);

    /// <summary>
    /// Resizes backend presentation state for one detached viewport.
    /// </summary>
    /// <param name="target">
    /// Updated viewport target.
    /// </param>
    void ResizeViewport(PlatformImGuiViewportTarget target);

    /// <summary>
    /// Consumes draw data for one detached viewport.
    /// </summary>
    /// <param name="target">
    /// Viewport destination.
    /// </param>
    /// <param name="drawData">
    /// Opaque native ImDrawData pointer valid for the current frame.
    /// </param>
    void RenderViewport(PlatformImGuiViewportTarget target, IntPtr drawData);

    /// <summary>
    /// Marks one detached viewport ready for presentation.
    /// </summary>
    /// <param name="target">
    /// Viewport destination.
    /// </param>
    void PresentViewport(PlatformImGuiViewportTarget target);

    /// <summary>
    /// Releases presentation state before its detached platform window is destroyed.
    /// </summary>
    /// <param name="target">
    /// Viewport being destroyed.
    /// </param>
    void DestroyViewport(PlatformImGuiViewportTarget target);
}
