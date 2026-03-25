namespace Inno.Rendering;

internal sealed class DefaultMaterialTextureResolver : IMaterialTextureResolver
{
    public bool TryResolve(Renderable renderable, Material material, out Texture? texture)
    {
        if (renderable is MeshRenderable meshRenderable
            && TryGetTextureFromPropertyBlock(meshRenderable.materialOverrides, out texture))
        {
            return true;
        }

        if (TryGetTextureFromPropertyBlock(material.overrides, out texture))
        {
            return true;
        }

        if (material is CustomMaterial customMaterial
            && TryGetTextureFromPropertyBlock(customMaterial.properties, out texture))
        {
            return true;
        }

        texture = material switch
        {
            StandardMaterial standard => standard.baseMap,
            UnlitMaterial unlit => unlit.colorMap,
            SpriteMaterial sprite => sprite.spriteTexture,
            SkyboxMaterial skybox => skybox.skyTexture,
            _ => null
        };

        return true;
    }

    private static bool TryGetTextureFromPropertyBlock(MaterialPropertyBlock? propertyBlock, out Texture? texture)
    {
        if (propertyBlock is null)
        {
            texture = null;
            return false;
        }

        if (propertyBlock.TryGetTexture("_MainTex", out texture)
            || propertyBlock.TryGetTexture("_BaseMap", out texture)
            || propertyBlock.TryGetTexture("baseMap", out texture)
            || propertyBlock.TryGetTexture("albedoMap", out texture)
            || propertyBlock.TryGetTexture("texture0", out texture))
        {
            return true;
        }

        texture = null;
        return false;
    }
}
