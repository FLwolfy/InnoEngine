namespace Inno.Graphics;

/// <summary>
/// Defines triangle culling mode.
/// </summary>
public enum GraphicsCullMode
{
    None = 0,
    Front,
    Back
}

/// <summary>
/// Defines fill rasterization mode.
/// </summary>
public enum GraphicsFillMode
{
    Solid = 0,
    Wireframe
}

/// <summary>
/// Defines depth compare operation.
/// </summary>
public enum GraphicsCompareOp
{
    Never = 0,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always
}

/// <summary>
/// Defines blend source and destination factors.
/// </summary>
public enum GraphicsBlendFactor
{
    Zero = 0,
    One,
    SrcColor,
    OneMinusSrcColor,
    DstColor,
    OneMinusDstColor,
    SrcAlpha,
    OneMinusSrcAlpha,
    DstAlpha,
    OneMinusDstAlpha
}

/// <summary>
/// Defines blend arithmetic operation.
/// </summary>
public enum GraphicsBlendOp
{
    Add = 0,
    Subtract,
    ReverseSubtract,
    Min,
    Max
}
