namespace Inno.Rendering;

internal interface IRenderableResolver
{
    bool TryResolve(Renderable renderable, Mesh builtinQuadMesh, Mesh builtinCubeMesh, out Mesh mesh, out Material material, out Transform transform);
}
