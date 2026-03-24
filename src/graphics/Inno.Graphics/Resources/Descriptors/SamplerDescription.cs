namespace Inno.Graphics;

/// <summary>
/// Describes sampler state creation.
/// </summary>
public sealed class SamplerDescription
{
    public bool anisotropicFiltering { get; init; }

    public float maxAnisotropy { get; init; } = 1.0f;
}
