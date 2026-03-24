namespace Inno.Rendering;

internal sealed class MaterialShaderResolverRegistry
{
    private readonly List<IMaterialShaderResolver> m_resolvers = [];

    public void Register(IMaterialShaderResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        m_resolvers.Add(resolver);
    }

    public string Resolve(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        foreach (var resolver in m_resolvers)
        {
            if (resolver.TryResolve(material, out var shaderName))
            {
                return shaderName;
            }
        }

        return "cubes";
    }
}
