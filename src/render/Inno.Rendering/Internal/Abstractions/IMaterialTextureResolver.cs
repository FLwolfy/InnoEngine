namespace Inno.Rendering;

internal interface IMaterialTextureResolver
{
    bool TryResolve(Renderable renderable, Material material, out Texture? texture);
}
