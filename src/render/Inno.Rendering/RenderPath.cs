namespace Inno.Rendering;

/// <summary>
/// Selects a built-in lighting path while allowing capability-aware fallback.
/// </summary>
public enum RenderPath
{
    /// <summary>Uses the active pipeline default and device capabilities.</summary>
    Automatic,
    /// <summary>Uses clustered forward lighting or its classic-forward fallback.</summary>
    ForwardPlus,
    /// <summary>Uses a geometry buffer followed by deferred lighting.</summary>
    Deferred
}

/// <summary>
/// Identifies a camera projection model.
/// </summary>
public enum CameraProjection
{
    /// <summary>Perspective projection with vertical field of view.</summary>
    Perspective,
    /// <summary>Orthographic projection with fixed vertical size.</summary>
    Orthographic
}

/// <summary>
/// Controls which camera target contents are initialized before rendering.
/// </summary>
public enum CameraClearMode
{
    /// <summary>Clears color and depth.</summary>
    Sky,
    /// <summary>Clears color and depth with the camera background color.</summary>
    Color,
    /// <summary>Preserves color while clearing depth.</summary>
    Depth,
    /// <summary>Preserves both color and depth when the target permits loading.</summary>
    Nothing
}

/// <summary>
/// Controls whether one renderer contributes to directional shadow maps.
/// </summary>
public enum ShadowCastingMode
{
    /// <summary>Never renders into a shadow map.</summary>
    Off,
    /// <summary>Renders into shadow maps using the material shadow pass.</summary>
    On,
    /// <summary>Renders only into shadow maps and not into camera color passes.</summary>
    ShadowsOnly
}
