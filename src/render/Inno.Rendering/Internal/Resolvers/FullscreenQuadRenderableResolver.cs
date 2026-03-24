namespace Inno.Rendering;

internal sealed class FullscreenQuadRenderableResolver : IRenderableResolver
{
    public bool TryResolve(Renderable renderable, Mesh builtinQuadMesh, Mesh builtinCubeMesh, out Mesh mesh, out Material material, out Transform transform)
    {
        _ = builtinCubeMesh;
        if (renderable is FullscreenQuadRenderable fullscreenQuadRenderable)
        {
            mesh = builtinQuadMesh;
            material = fullscreenQuadRenderable.material;
            transform = fullscreenQuadRenderable.transform;
            return true;
        }

        mesh = null!;
        material = null!;
        transform = Transform.identity;
        return false;
    }
}
