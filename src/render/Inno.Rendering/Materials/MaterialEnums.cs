namespace Inno.Rendering;

/// <summary>
/// Defines material surface shading model.
/// </summary>
public enum MaterialSurfaceType
{
    Opaque = 0,
    Transparent
}

/// <summary>
/// Defines material blending mode.
/// </summary>
public enum MaterialBlendMode
{
    Alpha = 0,
    Additive,
    Multiply
}

/// <summary>
/// Defines material face culling mode.
/// </summary>
public enum MaterialCullMode
{
    None = 0,
    Front,
    Back
}

/// <summary>
/// Defines material depth behavior.
/// </summary>
public enum MaterialDepthMode
{
    Disabled = 0,
    ReadWrite,
    ReadOnly
}

/// <summary>
/// Defines render pass material intent.
/// </summary>
public enum MaterialPassKind
{
    Forward = 0,
    ShadowCaster,
    DepthOnly,
    Unlit
}
