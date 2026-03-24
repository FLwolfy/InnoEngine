namespace Inno.Rendering;

internal interface IMaterialShaderResolver
{
    bool TryResolve(Material material, out string shaderName);
}
