namespace Inno.Rendering;

/// <summary>
/// Represents a post-process effect.
/// </summary>
public abstract class PostProcessEffect
{
    public bool enabled { get; set; } = true;
}

/// <summary>
/// Represents bloom settings.
/// </summary>
public sealed class BloomEffect : PostProcessEffect
{
    public float threshold { get; set; } = 1.0f;

    public float intensity { get; set; } = 0.5f;
}

/// <summary>
/// Represents tone mapping settings.
/// </summary>
public sealed class ToneMappingEffect : PostProcessEffect
{
    public float exposure { get; set; } = 1.0f;
}

/// <summary>
/// Represents color grading settings.
/// </summary>
public sealed class ColorGradingEffect : PostProcessEffect
{
    public float saturation { get; set; } = 1.0f;

    public float contrast { get; set; } = 1.0f;
}

/// <summary>
/// Represents FXAA settings.
/// </summary>
public sealed class FxaaEffect : PostProcessEffect
{
    public float qualitySubpix { get; set; } = 0.75f;
}

/// <summary>
/// Represents vignette settings.
/// </summary>
public sealed class VignetteEffect : PostProcessEffect
{
    public float intensity { get; set; } = 0.25f;

    public float smoothness { get; set; } = 0.5f;
}
