namespace Inno.Rendering;

/// <summary>
/// Represents texture sampler configuration.
/// </summary>
public sealed class TextureSampler
{
    public TextureWrapMode wrapU { get; init; } = TextureWrapMode.Repeat;

    public TextureWrapMode wrapV { get; init; } = TextureWrapMode.Repeat;

    public TextureFilterMode filter { get; init; } = TextureFilterMode.Trilinear;
}
