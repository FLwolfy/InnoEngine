using System.Text;
using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class DefaultMaterialParameterBinder : IMaterialParameterBinder
{
    public void Bind(IGraphicsCommandList commandList, Renderable renderable, Material material)
    {
        ArgumentNullException.ThrowIfNull(commandList);
        ArgumentNullException.ThrowIfNull(renderable);
        ArgumentNullException.ThrowIfNull(material);

        Span<float> raw = stackalloc float[4];
        raw[0] = (float)material.surfaceType;
        raw[1] = (float)material.blendMode;
        raw[2] = (float)material.cullMode;
        raw[3] = (float)material.depthMode;
        commandList.SetGlobalVector4("u_mat_renderState", raw);

        raw[0] = material.castShadows ? 1.0f : 0.0f;
        raw[1] = material.receiveShadows ? 1.0f : 0.0f;
        raw[2] = 0.0f;
        raw[3] = 0.0f;
        commandList.SetGlobalVector4("u_mat_shadowState", raw);

        BindBuiltinMaterialParameters(commandList, material);
        BindPropertyBlock(commandList, material.overrides);
        if (material is CustomMaterial customMaterial)
        {
            BindPropertyBlock(commandList, customMaterial.properties);
        }

        if (renderable is MeshRenderable meshRenderable)
        {
            BindPropertyBlock(commandList, meshRenderable.materialOverrides);
        }
    }

    private static void BindBuiltinMaterialParameters(IGraphicsCommandList commandList, Material material)
    {
        switch (material)
        {
            case StandardMaterial standard:
                SetColor(commandList, "u_mat_baseColor", standard.baseColor);
                SetScalar(commandList, "u_mat_metallic", standard.metallic);
                SetScalar(commandList, "u_mat_roughness", standard.roughness);
                SetScalar(commandList, "u_mat_normalScale", standard.normalScale);
                SetScalar(commandList, "u_mat_occlusionStrength", standard.occlusionStrength);
                SetColor(commandList, "u_mat_emissiveColor", standard.emissiveColor);
                SetScalar(commandList, "u_mat_alphaCutoff", standard.alphaCutoff);
                SetScalar(commandList, "u_mat_doubleSided", standard.doubleSided ? 1.0f : 0.0f);
                break;
            case UnlitMaterial unlit:
                SetColor(commandList, "u_mat_color", unlit.color);
                SetScalar(commandList, "u_mat_opacity", unlit.opacity);
                break;
            case SpriteMaterial sprite:
                SetColor(commandList, "u_mat_tint", sprite.tint);
                SetScalar(commandList, "u_mat_pixelSnap", sprite.pixelSnap ? 1.0f : 0.0f);
                break;
            case SkyboxMaterial skybox:
                SetScalar(commandList, "u_mat_exposure", skybox.exposure);
                break;
        }
    }

    private static void BindPropertyBlock(IGraphicsCommandList commandList, MaterialPropertyBlock? propertyBlock)
    {
        if (propertyBlock is null)
        {
            return;
        }

        foreach (var entry in propertyBlock.EnumerateProperties())
        {
            if (!TryConvertToVector4(entry.Value, out var value))
            {
                continue;
            }

            Span<float> raw = stackalloc float[4];
            raw[0] = value.x;
            raw[1] = value.y;
            raw[2] = value.z;
            raw[3] = value.w;
            commandList.SetGlobalVector4($"u_mat_{Sanitize(entry.Key)}", raw);
        }
    }

    private static bool TryConvertToVector4(object value, out Vector4 converted)
    {
        switch (value)
        {
            case float f:
                converted = new Vector4(f, 0.0f, 0.0f, 0.0f);
                return true;
            case int i:
                converted = new Vector4(i, 0.0f, 0.0f, 0.0f);
                return true;
            case bool b:
                converted = new Vector4(b ? 1.0f : 0.0f, 0.0f, 0.0f, 0.0f);
                return true;
            case Vector2 v2:
                converted = new Vector4(v2.x, v2.y, 0.0f, 0.0f);
                return true;
            case Vector3 v3:
                converted = new Vector4(v3.x, v3.y, v3.z, 0.0f);
                return true;
            case Vector4 v4:
                converted = v4;
                return true;
            case Color color:
                converted = new Vector4(color.r, color.g, color.b, color.a);
                return true;
            default:
                converted = default;
                return false;
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private static void SetScalar(IGraphicsCommandList commandList, string uniformName, float value)
    {
        Span<float> raw = stackalloc float[4];
        raw[0] = value;
        raw[1] = 0.0f;
        raw[2] = 0.0f;
        raw[3] = 0.0f;
        commandList.SetGlobalVector4(uniformName, raw);
    }

    private static void SetColor(IGraphicsCommandList commandList, string uniformName, Color color)
    {
        Span<float> raw = stackalloc float[4];
        raw[0] = color.r;
        raw[1] = color.g;
        raw[2] = color.b;
        raw[3] = color.a;
        commandList.SetGlobalVector4(uniformName, raw);
    }
}
