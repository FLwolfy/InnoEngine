namespace Inno.Rendering;

internal sealed class RenderableResolverRegistry
{
    private readonly List<IRenderableResolver> m_resolvers = [];

    public void Register(IRenderableResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        m_resolvers.Add(resolver);
    }

    public bool TryResolve(Renderable renderable, Mesh builtinQuadMesh, Mesh builtinCubeMesh, out Mesh mesh, out Material material, out Transform transform)
    {
        foreach (var resolver in m_resolvers)
        {
            if (resolver.TryResolve(renderable, builtinQuadMesh, builtinCubeMesh, out mesh, out material, out transform))
            {
                return true;
            }
        }

        mesh = null!;
        material = null!;
        transform = Transform.identity;
        return false;
    }
}
