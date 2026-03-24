namespace Inno.Rendering;

/// <summary>
/// Represents a post-process effect.
/// </summary>

public sealed class ToneMappingEffect : PostProcessEffect
{
    public float exposure { get; set; } = 1.0f;
}
