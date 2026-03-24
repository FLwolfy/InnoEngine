namespace Inno.Rendering;

/// <summary>
/// Defines depth test/write behavior for a material.
/// </summary>
public enum MaterialDepthMode
{
    Disabled = 0,
    ReadWrite,
    ReadOnly
}
