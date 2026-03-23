namespace Inno.Graphics;

/// <summary>
/// Represents texture and render target pixel formats.
/// </summary>
public enum PixelFormat
{
    Unknown = 0,
    R8Unorm,
    R8G8B8A8Unorm,
    B8G8R8A8Unorm,
    R16G16B16A16Float,
    R32G32B32A32Float,
    D24UnormS8Uint,
    D32Float
}

/// <summary>
/// Represents supported vertex element storage formats.
/// </summary>
public enum VertexFormat
{
    Float,
    Float2,
    Float3,
    Float4,
    Byte4Normalized,
    UShort2Normalized,
    UShort4Normalized
}

/// <summary>
/// Represents index buffer data formats.
/// </summary>
public enum IndexFormat
{
    UInt16 = 0,
    UInt32 = 1
}

/// <summary>
/// Represents multi-sampling levels.
/// </summary>
public enum SampleCount
{
    Count1 = 1,
    Count2 = 2,
    Count4 = 4,
    Count8 = 8
}
