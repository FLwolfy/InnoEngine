namespace Inno.Platform;

/// <summary>
/// Defines backend-neutral properties used to create a platform window.
/// </summary>
/// <returns>
/// A validated immutable window description.
/// </returns>
public readonly struct PlatformWindowOptions()
{
    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string title { get; init; } = "Inno Window";

    /// <summary>
    /// Gets the initial window width in platform-independent logical units.
    /// </summary>
    public int width { get; init; } = 1280;

    /// <summary>
    /// Gets the initial window height in platform-independent logical units.
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
