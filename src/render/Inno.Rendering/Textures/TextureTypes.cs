namespace Inno.Rendering;

/// <summary>
/// Represents texture pixel format.
/// </summary>
public enum TextureFormat
{
    Unknown = 0,
    Rgba8,
    Rgba16Float,
    Depth24Stencil8,
    Depth32
}

/// <summary>
/// Represents texture wrap mode.
/// </summary>
public enum TextureWrapMode
{
    Repeat = 0,
    Clamp,
    Mirror
}

/// <summary>
/// Represents texture sampling filter mode.
/// </summary>
public enum TextureFilterMode
{
    Nearest = 0,
    Bilinear,
    Trilinear
}

/// <summary>
/// Represents texture sampler configuration.
/// </summary>
public sealed class TextureSampler
{
    public TextureWrapMode wrapU { get; init; } = TextureWrapMode.Repeat;

    public TextureWrapMode wrapV { get; init; } = TextureWrapMode.Repeat;

    public TextureFilterMode filter { get; init; } = TextureFilterMode.Trilinear;
}
