using Inno.Core.Mathematics;

namespace Inno.Rendering;

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
