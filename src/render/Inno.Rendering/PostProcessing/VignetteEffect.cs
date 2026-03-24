namespace Inno.Rendering;

/// <summary>
/// Represents a post-process effect.
/// </summary>

public sealed class VignetteEffect : PostProcessEffect
{
    public float intensity { get; set; } = 0.25f;

    public float smoothness { get; set; } = 0.5f;
}
