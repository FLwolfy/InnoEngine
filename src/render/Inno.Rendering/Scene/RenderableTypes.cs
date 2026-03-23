using Inno.Rendering;

namespace Inno.Rendering;

/// <summary>
/// Defines renderable visibility state.
/// </summary>
public enum Visibility
{
    Visible = 0,
    Hidden
}

/// <summary>
/// Defines renderable shadow behavior.
/// </summary>
public enum ShadowMode
{
    Off = 0,
    CastOnly,
    ReceiveOnly,
    CastAndReceive
}

/// <summary>
/// Defines renderable motion vector behavior.
/// </summary>
public enum MotionMode
{
    Static = 0,
    Dynamic
}

/// <summary>
/// Represents a renderable scene object.
/// </summary>
public abstract class Renderable
{
    public string name { get; set; } = string.Empty;

    public Transform transform { get; set; } = Transform.identity;

    public uint layerMask { get; set; } = uint.MaxValue;

    public Visibility visibility { get; set; } = Visibility.Visible;

    public ShadowMode shadowMode { get; set; } = ShadowMode.CastAndReceive;

    public MotionMode motionMode { get; set; } = MotionMode.Static;

    public int sortingOrder { get; set; }
}

/// <summary>
/// Represents mesh-based renderable object.
/// </summary>
public sealed class MeshRenderable : Renderable
{
    public required Mesh mesh { get; init; }

    public required Material material { get; init; }

    public MaterialPropertyBlock? materialOverrides { get; set; }
}

/// <summary>
/// Represents screen-space or world-space sprite renderable.
/// </summary>
public sealed class SpriteRenderable : Renderable
{
    public required SpriteMaterial material { get; init; }
}

/// <summary>
/// Represents skybox renderable object.
/// </summary>
public sealed class SkyboxRenderable : Renderable
{
    public required SkyboxMaterial material { get; init; }
}

/// <summary>
/// Represents a fullscreen quad renderable object.
/// </summary>
public sealed class FullscreenQuadRenderable : Renderable
{
    public required Material material { get; init; }
}
