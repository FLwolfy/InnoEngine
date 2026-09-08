namespace Inno.Adapter.Input;

/// <summary>
/// Selects a built-in input backend without exposing its implementation types.
/// </summary>
public enum InputBackend
{
    /// <summary>
    /// Uses the SDL3 input implementation.
    /// </summary>
    Sdl3 = 0
}
