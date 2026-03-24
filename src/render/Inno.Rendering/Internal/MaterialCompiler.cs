
namespace Inno.Rendering;

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

