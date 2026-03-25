namespace Inno.Rendering;

internal sealed class MaterialTextureResolverRegistry
{
    private readonly List<IMaterialTextureResolver> m_resolvers = [];

    public void Register(IMaterialTextureResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        m_resolvers.Add(resolver);
    }

    public Texture? Resolve(Renderable renderable, Material material)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        ArgumentNullException.ThrowIfNull(material);

        foreach (var resolver in m_resolvers)
        {
            if (resolver.TryResolve(renderable, material, out var texture))
            {
                return texture;
            }
        }

        return null;
    }
}
