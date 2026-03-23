namespace Inno.Graphics;

/// <summary>
/// Defines bindable resource slot categories.
/// </summary>
public enum GraphicsBindingType
{
    UniformBuffer = 0,
    StorageBuffer,
    Texture,
    Sampler
}

/// <summary>
/// Defines intended buffer usage.
/// </summary>
public enum GraphicsBufferUsage
{
    Vertex = 0,
    Index,
    Uniform,
    Storage,
    Staging
}

/// <summary>
/// Defines CPU access behavior for buffers.
/// </summary>
public enum BufferCpuAccess
{
    None = 0,
    Read,
    Write,
    ReadWrite
}

/// <summary>
/// Defines intended texture usage.
/// </summary>
public enum TextureUsage
{
    Sampled = 0,
    RenderTarget,
    DepthStencil,
    Storage
}

/// <summary>
/// Defines texture dimension kind.
/// </summary>
public enum TextureDimension
{
    Texture2D = 0,
    Texture3D,
    TextureCube
}
