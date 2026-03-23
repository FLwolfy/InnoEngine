using Inno.Rendering;

namespace Inno.Rendering;

internal sealed class CompiledMesh
{
    public required Mesh source { get; init; }
}

internal sealed class CompiledMaterial
{
    public required Material source { get; init; }

    public required ShaderPermutationKey permutationKey { get; init; }
}

internal sealed class CompiledTexture
{
    public required Texture source { get; init; }
}

internal sealed class CompiledRenderTarget
{
    public required RenderTarget source { get; init; }
}

internal sealed class RenderResourceCache
{
    private readonly Dictionary<Mesh, CompiledMesh> m_meshes = new();
    private readonly Dictionary<Material, CompiledMaterial> m_materials = new();
    private readonly Dictionary<Texture, CompiledTexture> m_textures = new();
    private readonly Dictionary<RenderTarget, CompiledRenderTarget> m_targets = new();

    public CompiledMesh GetOrCompile(Mesh mesh, MeshCompiler compiler)
    {
        if (!m_meshes.TryGetValue(mesh, out var compiled))
        {
            compiled = compiler.Compile(mesh);
            m_meshes.Add(mesh, compiled);
        }

        return compiled;
    }

    public CompiledMaterial GetOrCompile(Material material, MaterialCompiler compiler)
    {
        if (!m_materials.TryGetValue(material, out var compiled))
        {
            compiled = compiler.Compile(material);
            m_materials.Add(material, compiled);
        }

        return compiled;
    }

    public CompiledTexture GetOrCreate(Texture texture)
    {
        if (!m_textures.TryGetValue(texture, out var compiled))
        {
            compiled = new CompiledTexture { source = texture };
            m_textures.Add(texture, compiled);
        }

        return compiled;
    }

    public CompiledRenderTarget GetOrCreate(RenderTarget target)
    {
        if (!m_targets.TryGetValue(target, out var compiled))
        {
            compiled = new CompiledRenderTarget { source = target };
            m_targets.Add(target, compiled);
        }

        return compiled;
    }
}

internal sealed class MaterialCompiler
{
    public CompiledMaterial Compile(Material material)
    {
        return new CompiledMaterial
        {
            source = material,
            permutationKey = ShaderPermutationKey.FromMaterial(material)
        };
    }
}

internal sealed class MeshCompiler
{
    public CompiledMesh Compile(Mesh mesh)
    {
        return new CompiledMesh
        {
            source = mesh
        };
    }
}
