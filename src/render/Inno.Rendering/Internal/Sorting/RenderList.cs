
namespace Inno.Rendering;

internal sealed class RenderList
{
    private readonly RenderScene m_scene;
    private readonly RenderView m_view;
    private readonly RenderQueue m_queue = new();

    public RenderList(RenderScene scene, RenderView view)
    {
        m_scene = scene;
        m_view = view;
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

            var material = renderable switch
            {
                MeshRenderable meshRenderable => meshRenderable.material,
                SpriteRenderable spriteRenderable => spriteRenderable.material,
                SkyboxRenderable skyboxRenderable => skyboxRenderable.material,
                FullscreenQuadRenderable fullscreenQuadRenderable => fullscreenQuadRenderable.material,
                _ => null
            };

            var isTransparent = material?.surfaceType == MaterialSurfaceType.Transparent;

            if (filter == RenderItemFilter.Skybox && renderable is not SkyboxRenderable)
            {
                continue;
            }

            if (filter == RenderItemFilter.Ui && renderable is not SpriteRenderable)
            {
                continue;
            }

            if (filter == RenderItemFilter.PostProcess && renderable is not FullscreenQuadRenderable)
            {
                continue;
            }

            if (filter == RenderItemFilter.Gizmo && renderable is not FullscreenQuadRenderable)
            {
                continue;
            }

            if (filter == RenderItemFilter.ObjectPicking && renderable is not MeshRenderable)
            {
                continue;
            }

            if (filter == RenderItemFilter.Opaque && isTransparent)
            {
                continue;
            }

            if (filter == RenderItemFilter.Transparent && !isTransparent)
            {
                continue;
            }

            if (filter == RenderItemFilter.ShadowCasters)
            {
                if (renderable is not MeshRenderable)
                {
                    continue;
                }

                if (renderable.shadowMode is ShadowMode.Off or ShadowMode.ReceiveOnly)
                {
                    continue;
                }

                if (material is not null && !material.castShadows)
                {
                    continue;
                }
            }

            if (filter == RenderItemFilter.DepthOnly)
            {
                if (renderable is not MeshRenderable)
                {
                    continue;
                }

                if (isTransparent == true)
                {
                    continue;
                }
            }

            m_queue.Add(new RenderItem
            {
                renderable = renderable,
                sortKey = new RenderSortKey((ulong)(int.MaxValue - renderable.sortingOrder))
            });
        }

        m_queue.Sort();
    }
}
