namespace Inno.Rendering;

internal sealed class CustomMaterialShaderResolver : IMaterialShaderResolver
{
    public bool TryResolve(Material material, out string shaderName)
    {
        if (material is CustomMaterial custom && !string.IsNullOrWhiteSpace(custom.shaderName))
        {
            shaderName = custom.shaderName.Trim();
            return true;
        }

        shaderName = string.Empty;
        return false;
    }
}
