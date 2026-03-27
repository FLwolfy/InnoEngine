namespace Inno.Platform;

/// <summary>
/// Options used when creating a <see cref="PlatformWindow"/>.
/// </summary>
public readonly struct PlatformWindowOptions()
{
    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string title { get; init; } = "Inno Window";

    /// <summary>
    /// Gets the initial window width in pixels.
    /// </summary>
    public int width { get; init; } = 1280;

    /// <summary>
    /// Gets the initial window height in pixels.
    /// </summary>
    public int height { get; init; } = 720;

    /// <summary>
    /// Gets whether the window is user-resizable.
    /// </summary>
    public bool resizable { get; init; } = true;

    /// <summary>
    /// Gets whether high pixel density is requested for the window.
    /// </summary>
    public bool highPixelDensity { get; init; } = true;
}
