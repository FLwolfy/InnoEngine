namespace Inno.Rendering;

/// <summary>
/// Represents a post-process effect.
/// </summary>

public sealed class BloomEffect : PostProcessEffect
{
    public float threshold { get; set; } = 1.0f;

    public float intensity { get; set; } = 0.5f;
}
