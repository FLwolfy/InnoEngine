namespace Inno.Adapter.Platform;

/// <summary>
/// Selects a built-in platform backend without exposing its implementation types.
/// </summary>
public enum PlatformBackend
{
    /// <summary>
    /// Uses the SDL3 platform implementation.
    /// </summary>
    Sdl3 = 0
}
