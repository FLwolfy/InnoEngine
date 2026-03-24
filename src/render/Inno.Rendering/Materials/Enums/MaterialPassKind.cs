namespace Inno.Rendering;

/// <summary>
/// Defines material pass intent.
/// </summary>
public enum MaterialPassKind
{
    Forward = 0,
    ShadowCaster,
    DepthOnly,
    Unlit
}
