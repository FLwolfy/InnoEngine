namespace Inno.Rendering;

internal sealed class SkyboxRenderableResolver : IRenderableResolver
{
    public bool TryResolve(Renderable renderable, Mesh builtinQuadMesh, Mesh builtinCubeMesh, out Mesh mesh, out Material material, out Transform transform)
    {
        _ = builtinQuadMesh;
        if (renderable is SkyboxRenderable skyboxRenderable)
        {
            mesh = builtinCubeMesh;
            material = skyboxRenderable.material;
            transform = skyboxRenderable.transform;
            return true;
        }

        mesh = null!;
        material = null!;
        transform = Transform.identity;
        return false;
    }
}
