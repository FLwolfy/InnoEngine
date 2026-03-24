namespace Inno.Rendering;

internal sealed class StandardMaterialShaderResolver : IMaterialShaderResolver
{
    public bool TryResolve(Material material, out string shaderName)
    {
        if (material is StandardMaterial)
        {
            shaderName = "lit";
            return true;
        }

        shaderName = string.Empty;
        return false;
    }
}
