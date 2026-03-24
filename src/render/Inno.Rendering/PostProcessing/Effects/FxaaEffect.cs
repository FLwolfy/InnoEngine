namespace Inno.Rendering;

/// <summary>
/// Represents a post-process effect.
/// </summary>

public sealed class FxaaEffect : PostProcessEffect
{
    public float qualitySubpix { get; set; } = 0.75f;
}
