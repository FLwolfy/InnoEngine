using System;
using Inno.Platform;
using Inno.Rendering.Core;

namespace Inno.Rendering.Bgfx;

/// <summary>
/// Configures BGFX initialization without exposing native BGFX structures.
/// </summary>
public sealed class BgfxDeviceOptions
{
    private int m_backbufferWidth = 1;
    private int m_backbufferHeight = 1;
    private int m_deferredDestroyFrames = 6;

    /// <summary>Gets or sets the preferred renderer, or <see langword="null"/> for platform default.</summary>
    public GraphicsBackend? preferredBackend { get; set; }

    /// <summary>Gets or sets the platform window used as the main swapchain surface.</summary>
    public PlatformWindow? window { get; set; }

    /// <summary>Gets or sets the initial backbuffer width in physical pixels when no window supplies one.</summary>
    public int backbufferWidth
    {
        get => window?.pixelWidth ?? m_backbufferWidth;
        set => m_backbufferWidth = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Gets or sets the initial backbuffer height in physical pixels when no window supplies one.</summary>
    public int backbufferHeight
    {
        get => window?.pixelHeight ?? m_backbufferHeight;
        set => m_backbufferHeight = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Gets or sets whether submission waits for display synchronization.</summary>
    public bool verticalSync { get; set; } = true;

    /// <summary>Gets or sets whether the main backbuffer performs sRGB encoding.</summary>
    public bool sRgbBackbuffer { get; set; } = true;

    /// <summary>
    /// Gets or sets whether BGFX rendering is driven inline on the API thread.
    /// </summary>
    /// <remarks>
    /// This is intended for Noop tests and hosts that cannot provide a dedicated
    /// BGFX render thread. Production windowed hosts normally leave it disabled.
    /// </remarks>
    public bool forceSingleThreaded { get; set; }

    /// <summary>Gets or sets the number of submitted frames before queued native destruction.</summary>
    public int deferredDestroyFrames
    {
        get => m_deferredDestroyFrames;
        set => m_deferredDestroyFrames = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }
}
