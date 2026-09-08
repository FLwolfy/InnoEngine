namespace Inno.Adapter.Rendering;

/// <summary>
/// Selects a built-in rendering backend without exposing its implementation types.
/// </summary>
public enum RenderingBackend
{
    /// <summary>
    /// Uses the BGFX rendering implementation.
    /// </summary>
    Bgfx = 0
}
