
namespace Inno.Rendering;

internal readonly record struct ShaderPermutationKey(string surfaceType, string blendMode, string cullMode)
{
    public static ShaderPermutationKey FromMaterial(Material material)
    {
        return new ShaderPermutationKey(material.surfaceType.ToString(), material.blendMode.ToString(), material.cullMode.ToString());
    }
}

