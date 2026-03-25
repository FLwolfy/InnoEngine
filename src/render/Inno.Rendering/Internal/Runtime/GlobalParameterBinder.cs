using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class GlobalParameterBinder
{
    public void ApplyGlobalLightUniform(IGraphicsCommandList commandList, RenderScene scene)
    {
        var lightColor = Color.WHITE;
        var lightIntensity = 0.0f;
        foreach (var light in scene.lights.items)
        {
            if (light is not DirectionalLight directional || !directional.enabled)
            {
                continue;
            }

            lightColor = directional.color;
            lightIntensity = directional.intensity;
            break;
        }

        Span<float> lightRaw = stackalloc float[4];
        lightRaw[0] = lightColor.r;
        lightRaw[1] = lightColor.g;
        lightRaw[2] = lightColor.b;
        lightRaw[3] = lightIntensity;
        commandList.SetGlobalVector4("u_globalLight", lightRaw);

        var lightDirection = ResolveDirectionalLightDirection(scene);
        Span<float> lightDirRaw = stackalloc float[4];
        lightDirRaw[0] = lightDirection.x;
        lightDirRaw[1] = lightDirection.y;
        lightDirRaw[2] = lightDirection.z;
        lightDirRaw[3] = 0.0f;
        commandList.SetGlobalVector4("u_mainLightDir", lightDirRaw);
    }

    public void ApplyCameraUniform(IGraphicsCommandList commandList, Camera camera)
    {
        var p = camera.transform.position;
        var f = Vector3.NormalizeSafe(camera.transform.forward);
        Span<float> cameraRaw = stackalloc float[4];
        cameraRaw[0] = p.x;
        cameraRaw[1] = p.y;
        cameraRaw[2] = p.z;
        cameraRaw[3] = 1.0f;
        commandList.SetGlobalVector4("u_cameraWorldPos", cameraRaw);

        cameraRaw[0] = f.x;
        cameraRaw[1] = f.y;
        cameraRaw[2] = f.z;
        cameraRaw[3] = 0.0f;
        commandList.SetGlobalVector4("u_cameraForward", cameraRaw);
    }

    public void ApplyShadowUniforms(
        IGraphicsCommandList commandList,
        RenderScene scene,
        bool shadowMapReady,
        int shadowCascadeCount,
        ShadowCascadeData[] shadowCascades,
        int shadowMapSize)
    {
        var shadowsEnabled = scene.settings.enableShadows;
        var shadowSettings = ResolveDirectionalShadowSettings(scene);
        if (!shadowsEnabled || !shadowMapReady || !shadowSettings.enabled)
        {
            Span<float> disabled = stackalloc float[4];
            disabled[0] = 0.0f;
            disabled[1] = 0.0f;
            disabled[2] = 1.0f;
            disabled[3] = 0.0f;
            commandList.SetGlobalVector4("u_shadowParams", disabled);
            return;
        }

        SetMatrixRows(commandList, "u_lightViewProj0_", shadowCascades[0].viewProjection);
        SetMatrixRows(commandList, "u_lightViewProj1_", shadowCascades[1].viewProjection);

        Span<float> v = stackalloc float[4];
        v[0] = shadowCascadeCount;
        v[1] = shadowCascades[0].splitDistance;
        v[2] = shadowCascades[1].splitDistance;
        v[3] = 0.0f;
        commandList.SetGlobalVector4("u_shadowCascadeInfo", v);

        v[0] = shadowCascades[0].atlasScaleBias.x;
        v[1] = shadowCascades[0].atlasScaleBias.y;
        v[2] = shadowCascades[0].atlasScaleBias.z;
        v[3] = shadowCascades[0].atlasScaleBias.w;
        commandList.SetGlobalVector4("u_shadowCascadeData0", v);

        v[0] = shadowCascades[1].atlasScaleBias.x;
        v[1] = shadowCascades[1].atlasScaleBias.y;
        v[2] = shadowCascades[1].atlasScaleBias.z;
        v[3] = shadowCascades[1].atlasScaleBias.w;
        commandList.SetGlobalVector4("u_shadowCascadeData1", v);

        v[0] = MathF.Max(0.0f, shadowSettings.depthBias);
        v[1] = Math.Clamp(shadowSettings.strength, 0.0f, 1.0f);
        v[2] = 1.0f / Math.Max(1, shadowMapSize);
        v[3] = Math.Max(0.0f, shadowSettings.pcfRadius);
        commandList.SetGlobalVector4("u_shadowParams", v);
    }

    public void ApplyShadowReceiverUniform(IGraphicsCommandList commandList, Renderable renderable, Material material)
    {
        var receiveShadows = material.receiveShadows
            && renderable.shadowMode is ShadowMode.CastAndReceive or ShadowMode.ReceiveOnly;
        Span<float> v = stackalloc float[4];
        v[0] = receiveShadows ? 1.0f : 0.0f;
        v[1] = 0.0f;
        v[2] = 0.0f;
        v[3] = 0.0f;
        commandList.SetGlobalVector4("u_shadowReceiver", v);
    }

    public void SetMatrixRows(IGraphicsCommandList commandList, string uniformPrefix, Matrix matrix)
    {
        Span<float> row = stackalloc float[4];
        row[0] = matrix.m11;
        row[1] = matrix.m21;
        row[2] = matrix.m31;
        row[3] = matrix.m41;
        commandList.SetGlobalVector4($"{uniformPrefix}0", row);

        row[0] = matrix.m12;
        row[1] = matrix.m22;
        row[2] = matrix.m32;
        row[3] = matrix.m42;
        commandList.SetGlobalVector4($"{uniformPrefix}1", row);

        row[0] = matrix.m13;
        row[1] = matrix.m23;
        row[2] = matrix.m33;
        row[3] = matrix.m43;
        commandList.SetGlobalVector4($"{uniformPrefix}2", row);

        row[0] = matrix.m14;
        row[1] = matrix.m24;
        row[2] = matrix.m34;
        row[3] = matrix.m44;
        commandList.SetGlobalVector4($"{uniformPrefix}3", row);
    }

    private static Vector3 ResolveDirectionalLightDirection(RenderScene scene)
    {
        foreach (var light in scene.lights.items)
        {
            if (light is DirectionalLight directional && directional.enabled)
            {
                return Vector3.NormalizeSafe(directional.direction);
            }
        }

        return Vector3.NormalizeSafe(new Vector3(-0.5f, -1.0f, -0.3f));
    }

    private static LightShadowSettings ResolveDirectionalShadowSettings(RenderScene scene)
    {
        foreach (var light in scene.lights.items)
        {
            if (light is DirectionalLight directional && directional.enabled)
            {
                return directional.shadows;
            }
        }

        return LightShadowSettings.@default with { enabled = false };
    }
}
