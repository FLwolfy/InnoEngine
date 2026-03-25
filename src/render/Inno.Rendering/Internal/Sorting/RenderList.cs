
namespace Inno.Rendering;

internal sealed class RenderList
{
    private readonly RenderScene m_scene;
    private readonly RenderView m_view;
    private readonly RenderItemClassifierRegistry m_classifiers;
    private readonly RenderQueue m_queue = new();
    private static readonly RenderItemClassifierRegistry s_defaultClassifiers = CreateDefaultClassifiers();

    public RenderList(RenderScene scene, RenderView view, RenderItemClassifierRegistry? classifiers = null)
    {
        m_scene = scene;
        m_view = view;
        m_classifiers = classifiers ?? s_defaultClassifiers;
    }

    public IReadOnlyList<RenderItem> items => m_queue.items;

    public void Build(RenderItemFilter filter)
    {
        m_queue.Clear();
        var viewMask = m_view.layerMask.value;
        foreach (var renderable in m_scene.renderables.items)
        {
            if (renderable.visibility != Visibility.Visible)
            {
                continue;
            }

            if ((renderable.layerMask & viewMask) == 0)
            {
                continue;
            }

            var material = ResolveMaterial(renderable);
            if (!m_classifiers.ShouldInclude(filter, renderable, material))
            {
                continue;
            }

            m_queue.Add(new RenderItem
            {
                renderable = renderable,
                sortKey = new RenderSortKey((ulong)(int.MaxValue - renderable.sortingOrder))
            });
        }

        m_queue.Sort();
    }

    private static Material? ResolveMaterial(Renderable renderable)
    {
        return renderable switch
        {
            MeshRenderable meshRenderable => meshRenderable.material,
            SpriteRenderable spriteRenderable => spriteRenderable.material,
            SkyboxRenderable skyboxRenderable => skyboxRenderable.material,
            FullscreenQuadRenderable fullscreenQuadRenderable => fullscreenQuadRenderable.material,
            _ => null
        };
    }

    private static RenderItemClassifierRegistry CreateDefaultClassifiers()
    {
        var registry = new RenderItemClassifierRegistry();
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.Skybox, static (renderable, _) => renderable is SkyboxRenderable));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.Ui, static (renderable, _) => renderable is SpriteRenderable));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.PostProcess, static (renderable, _) => renderable is FullscreenQuadRenderable));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.Gizmo, static (renderable, _) => renderable is FullscreenQuadRenderable));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.ObjectPicking, static (renderable, _) => renderable is MeshRenderable));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.Opaque, static (_, material) => material?.surfaceType != MaterialSurfaceType.Transparent));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.Transparent, static (_, material) => material?.surfaceType == MaterialSurfaceType.Transparent));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.ShadowCasters, static (renderable, material) =>
        {
            if (renderable is not MeshRenderable)
            {
                return false;
            }

            if (renderable.shadowMode is ShadowMode.Off or ShadowMode.ReceiveOnly)
            {
                return false;
            }

            return material is null || material.castShadows;
        }));
        registry.Register(new DefaultRenderItemClassifier(RenderItemFilter.DepthOnly, static (renderable, material) =>
            renderable is MeshRenderable && material?.surfaceType != MaterialSurfaceType.Transparent));
        return registry;
    }
}
