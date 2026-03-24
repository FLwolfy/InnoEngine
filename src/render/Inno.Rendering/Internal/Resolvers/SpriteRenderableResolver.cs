namespace Inno.Rendering;

internal sealed class SpriteRenderableResolver : IRenderableResolver
{
    public bool TryResolve(Renderable renderable, Mesh builtinQuadMesh, Mesh builtinCubeMesh, out Mesh mesh, out Material material, out Transform transform)
    {
        _ = builtinCubeMesh;
        if (renderable is SpriteRenderable spriteRenderable)
        {
            mesh = builtinQuadMesh;
            material = spriteRenderable.material;
            transform = spriteRenderable.transform;
            return true;
        }

        mesh = null!;
        material = null!;
        transform = Transform.identity;
        return false;
    }
}
