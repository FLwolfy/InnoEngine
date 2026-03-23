using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents environment-level scene rendering properties.
/// </summary>
public sealed class SceneEnvironment
{
    public Color ambientColor { get; set; } = Color.BLACK;

    public float ambientIntensity { get; set; } = 1.0f;
}

/// <summary>
/// Represents scene-level rendering settings.
/// </summary>
public sealed class SceneRenderSettings
{
    public bool enableShadows { get; set; } = true;

    public bool enableFog { get; set; }
}

/// <summary>
/// Represents a render scene with renderables and lights.
/// </summary>
public sealed class RenderScene
{
    public SceneEnvironment environment { get; } = new();

    public SceneRenderSettings settings { get; } = new();

    public RenderableCollection renderables { get; } = new();

    public LightCollection lights { get; } = new();

    public void Add(Renderable renderable) => renderables.Add(renderable);

    public bool Remove(Renderable renderable) => renderables.Remove(renderable);

    public void Add(Light light) => lights.Add(light);

    public bool Remove(Light light) => lights.Remove(light);
}
