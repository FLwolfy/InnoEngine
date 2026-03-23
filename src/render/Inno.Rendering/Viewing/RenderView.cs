using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents a renderable camera view description.
/// </summary>
public sealed class RenderView
{
    public required Camera camera { get; init; }

    public Viewport viewport { get; private set; } = new(0, 0, 1, 1);

    public ClearSettings clear { get; private set; } = ClearSettings.Solid(Color.BLACK);

    public CullingSettings culling { get; set; } = CullingSettings.@default;

    public RenderLayerMask layerMask { get; set; } = RenderLayerMask.everything;

    public bool enablePostProcessing { get; set; } = true;

    public bool enableGizmos { get; set; }

    public bool enableDebugOverlays { get; set; }

    public static RenderView ForCamera(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return new RenderView
        {
            camera = camera
        };
    }

    public RenderView WithViewport(int x, int y, int width, int height)
    {
        viewport = new Viewport(x, y, width, height);
        return this;
    }

    public RenderView WithClear(ClearSettings clear)
    {
        this.clear = clear;
        return this;
    }
}
