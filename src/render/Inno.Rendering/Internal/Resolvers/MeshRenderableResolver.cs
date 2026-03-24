namespace Inno.Rendering;

internal sealed class MeshRenderableResolver : IRenderableResolver
{
    public bool TryResolve(Renderable renderable, Mesh builtinQuadMesh, Mesh builtinCubeMesh, out Mesh mesh, out Material material, out Transform transform)
    {
        _ = builtinQuadMesh;
        _ = builtinCubeMesh;
        if (renderable is MeshRenderable meshRenderable)
        {
            mesh = meshRenderable.mesh;
            material = meshRenderable.material;
            transform = meshRenderable.transform;
            return true;
        }

        mesh = null!;
        material = null!;
        transform = Transform.identity;
        return false;
    }
}
