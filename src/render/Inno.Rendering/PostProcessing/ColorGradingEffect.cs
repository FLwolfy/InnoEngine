namespace Inno.Rendering;

/// <summary>
/// Represents a post-process effect.
/// </summary>

public sealed class ColorGradingEffect : PostProcessEffect
{
    public float saturation { get; set; } = 1.0f;

    public float contrast { get; set; } = 1.0f;
}
