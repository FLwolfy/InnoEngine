using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;
using Inno.Rendering.ImGui;
using Inno.Rendering.Pipelines;

namespace Inno.Editor.Application;

internal static class EditorShaderBootstrap
{
    private const int C_MAX_LOCAL_LIGHTS = 8;

    internal static GraphicsPipelineDescriptor Compile(
        GraphicsCapabilities capabilities,
        string assetsDirectory,
        RenderPipelineArtifactRegistry artifacts)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsDirectory);
        ArgumentNullException.ThrowIfNull(artifacts);
        Directory.CreateDirectory(assetsDirectory);
        ShaderTargetPlatform platform = OperatingSystem.IsWindows()
            ? ShaderTargetPlatform.WindowsX64
            : OperatingSystem.IsMacOS()
                ? ShaderTargetPlatform.MacOSArm64
                : throw new PlatformNotSupportedException(
                    "The first rendering milestone supports Windows x64 and macOS arm64 editors.");
        var target = new ShaderCompileTarget(
            RendererProfileCatalog.Resolve(platform, capabilities),
            capabilities,
            optimize: false,
            debugInformation: true);
        var compiler = new ShaderCompiler();

        CompiledShaderArtifact imgui = CompileRequired(
            compiler,
            CreateImGuiModule(),
            target,
            assetsDirectory);
        CompiledShaderPass imguiPass = RequirePass(imgui, "ImGui");
        GraphicsPipelineDescriptor imguiPipeline = new(
            RequireStage(imguiPass, ShaderStage.Vertex).bytes.Span,
            RequireStage(imguiPass, ShaderStage.Fragment).bytes.Span,
            [new RenderShaderBindingDescriptor(
                new RenderBindingId("s_tex"),
                RenderShaderBindingKind.Texture,
                slot: 0)],
            BgfxImGuiRenderer.vertexLayout,
            new RenderRasterState
            {
                cull = RenderCullMode.None,
                depthCompare = RenderDepthCompare.Always,
                depthWrite = false,
                blend = RenderBlendMode.Alpha,
                multisampling = true
            });

        CompiledShaderArtifact toneMap = CompileRequired(
            compiler,
            CreateToneMapModule(includeBloom: false),
            target,
            assetsDirectory);
        artifacts.InstallOperation(BuiltinPipelineOperations.ToneMap, toneMap, "ToneMap");
        CompiledShaderArtifact toneMapBloom = CompileRequired(
            compiler,
            CreateToneMapModule(includeBloom: true),
            target,
            assetsDirectory);
        artifacts.InstallOperation(BuiltinPipelineOperations.ToneMapBloom, toneMapBloom, "ToneMapBloom");
        InstallOperation(
            compiler,
            target,
            assetsDirectory,
            artifacts,
            BuiltinPipelineOperations.ClusterLights,
            CreateClusterLightsModule(),
            "ClusterLights");
        InstallOperation(
            compiler,
            target,
            assetsDirectory,
            artifacts,
            BuiltinPipelineOperations.DeferredLighting,
            CreateDeferredLightingModule(),
            "DeferredLighting");
        InstallOperation(
            compiler,
            target,
            assetsDirectory,
            artifacts,
            BuiltinPipelineOperations.Sky,
            CreateSkyModule(),
            "Sky");
        InstallOperation(
            compiler,
            target,
            assetsDirectory,
            artifacts,
            BuiltinPipelineOperations.BloomDownsample,
            CreateBloomModule("BloomDownsample", "inno_scene_color", bloomDownsample: true),
            "BloomDownsample");
        InstallOperation(
            compiler,
            target,
            assetsDirectory,
            artifacts,
            BuiltinPipelineOperations.BloomUpsample,
            CreateBloomModule("BloomUpsample", "inno_bloom", bloomDownsample: false),
            "BloomUpsample");
        return imguiPipeline;
    }

    private static void InstallOperation(
        ShaderCompiler compiler,
        ShaderCompileTarget target,
        string sourceRoot,
        RenderPipelineArtifactRegistry artifacts,
        string operationId,
        ShaderIRModule module,
        string passName)
    {
        CompiledShaderArtifact artifact = CompileRequired(compiler, module, target, sourceRoot);
        artifacts.InstallOperation(operationId, artifact, passName);
    }

    private static CompiledShaderArtifact CompileRequired(
        ShaderCompiler compiler,
        ShaderIRModule module,
        ShaderCompileTarget target,
        string sourceRoot)
    {
        ShaderCompilationResult result = compiler.CompileAsync(
                module,
                target,
                ShaderVariantKey.empty,
                sourceRoot)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        if (result.artifact is not null && result.succeeded)
        {
            return result.artifact;
        }

        string errors = string.Join(
            Environment.NewLine,
            result.diagnostics.Select(static value => $"[{value.code}] {value.message}"));
        throw new InvalidOperationException(
            $"Editor built-in shader '{module.definition.name}' failed to compile:{Environment.NewLine}{errors}");
    }

    private static ShaderIRModule CreateImGuiModule()
    {
        var pass = new ShaderPassDefinition(
            "ImGui",
            BuiltinShaderPassTags.Fullscreen,
            "Builtin/ImGui.vs.sc",
            "Builtin/ImGui.fs.sc",
            null,
            "Builtin/ImGui.varying.def.sc",
            renderState: new ShaderRenderState
            {
                cull = ShaderCullMode.None,
                depthCompare = ShaderCompareFunction.Always,
                depthWrite = false,
                blend = ShaderBlendMode.Alpha
            });
        var definition = new ShaderDefinition(
            "Inno/Builtin/ImGui",
            [new ShaderPropertyDefinition(
                new ShaderPropertyId("s_tex"),
                "Texture",
                ShaderPropertyType.Texture2D,
                ShaderStage.Fragment,
                "null")],
            [],
            [pass]);
        return new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [
                    Stage(ShaderStage.Vertex, BgfxImGuiShaderSource.vertex, "Builtin/ImGui.vs.sc", pass.name),
                    Stage(ShaderStage.Fragment, BgfxImGuiShaderSource.fragment, "Builtin/ImGui.fs.sc", pass.name)
                ],
                BgfxImGuiShaderSource.varying)]);
    }

    private static ShaderIRModule CreateToneMapModule(bool includeBloom)
    {
        const string varying = "vec2 v_texcoord0 : TEXCOORD0;";
        const string vertex = """
            $output v_texcoord0
            #include <bgfx_shader.sh>

            void main()
            {
                float x = -1.0 + float((gl_VertexID & 1) << 2);
                float y =  1.0 - float((gl_VertexID & 2) << 1);
                gl_Position = vec4(x, y, 0.0, 1.0);
                v_texcoord0 = vec2((x + 1.0) * 0.5, (1.0 - y) * 0.5);
            }
            """;
        string bloomDeclaration = includeBloom ? "SAMPLER2D(inno_bloom, 1);" : string.Empty;
        string bloomSample = includeBloom
            ? "color += texture2D(inno_bloom, v_texcoord0).rgb * 0.12;"
            : string.Empty;
        string fragment = $$"""
            $input v_texcoord0
            #include <bgfx_shader.sh>
            SAMPLER2D(inno_scene_color, 0);
            {{bloomDeclaration}}
            uniform vec4 inno_exposure;

            vec3 TonemapAces(vec3 color)
            {
                const float a = 2.51;
                const float b = 0.03;
                const float c = 2.43;
                const float d = 0.59;
                const float e = 0.14;
                return saturate((color * (a * color + b)) / (color * (c * color + d) + e));
            }

            void main()
            {
                vec3 color = texture2D(inno_scene_color, v_texcoord0).rgb;
                {{bloomSample}}
                gl_FragColor = vec4(TonemapAces(color * exp2(inno_exposure.x)), 1.0);
            }
            """;
        var pass = new ShaderPassDefinition(
            includeBloom ? "ToneMapBloom" : "ToneMap",
            BuiltinShaderPassTags.Fullscreen,
            "Builtin/ToneMap.vs.sc",
            "Builtin/ToneMap.fs.sc",
            null,
            "Builtin/ToneMap.varying.def.sc",
            renderState: new ShaderRenderState
            {
                cull = ShaderCullMode.None,
                depthCompare = ShaderCompareFunction.Always,
                depthWrite = false
            });
        var properties = new List<ShaderPropertyDefinition>
        {
                new ShaderPropertyDefinition(
                    new ShaderPropertyId("inno_scene_color"),
                    "Scene Color",
                    ShaderPropertyType.Texture2D,
                    ShaderStage.Fragment,
                    "null"),
                new ShaderPropertyDefinition(
                    new ShaderPropertyId("inno_exposure"),
                    "Exposure",
                    ShaderPropertyType.Float,
                    ShaderStage.Fragment,
                    "0")
        };
        if (includeBloom)
        {
            properties.Add(new ShaderPropertyDefinition(
                new ShaderPropertyId("inno_bloom"),
                "Bloom",
                ShaderPropertyType.Texture2D,
                ShaderStage.Fragment,
                "null"));
        }

        var definition = new ShaderDefinition(
            includeBloom ? "Inno/Builtin/ToneMapBloom" : "Inno/Builtin/ToneMap",
            properties,
            [],
            [pass]);
        return new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [
                    Stage(ShaderStage.Vertex, vertex, "Builtin/ToneMap.vs.sc", pass.name),
                    Stage(ShaderStage.Fragment, fragment, "Builtin/ToneMap.fs.sc", pass.name)
                ],
                varying)]);
    }

    private static ShaderIRModule CreateClusterLightsModule()
    {
        string compute = $$"""
            #include "bgfx_compute.sh"
            BUFFER_RW(inno_cluster_grid, uint, 0);
            BUFFER_RW(inno_cluster_light_indices, uint, 1);
            uniform vec4 inno_light_count;
            uniform vec4 inno_cluster_parameters;
            uniform vec4 inno_cluster_depth_parameters;
            {{LocalLightDeclarations()}}

            bool InnoIntersectsCluster(vec4 positionRange, vec3 clusterCoordinate)
            {
                vec3 viewPosition = mul(u_view, vec4(positionRange.xyz, 1.0)).xyz;
                float radius = max(positionRange.w, 0.0);
                float sliceNear = inno_cluster_depth_parameters.x * pow(
                    inno_cluster_depth_parameters.y / inno_cluster_depth_parameters.x,
                    clusterCoordinate.z / inno_cluster_parameters.z);
                float sliceFar = inno_cluster_depth_parameters.x * pow(
                    inno_cluster_depth_parameters.y / inno_cluster_depth_parameters.x,
                    (clusterCoordinate.z + 1.0) / inno_cluster_parameters.z);
                if (viewPosition.z + radius < sliceNear || viewPosition.z - radius > sliceFar)
                {
                    return false;
                }

                float conservativeDepth = max(viewPosition.z - radius, inno_cluster_depth_parameters.x);
                vec4 clipPosition = mul(u_proj, vec4(viewPosition, 1.0));
                if (abs(clipPosition.w) < 0.00001)
                {
                    return false;
                }

                vec2 center = clipPosition.xy / clipPosition.w;
                vec2 projectedRadius = vec2(abs(u_proj[0][0]), abs(u_proj[1][1]))
                    * radius / conservativeDepth;
                vec2 tileMinimum = vec2(
                    clusterCoordinate.x / inno_cluster_parameters.x * 2.0 - 1.0,
                    1.0 - (clusterCoordinate.y + 1.0) / inno_cluster_parameters.y * 2.0);
                vec2 tileMaximum = vec2(
                    (clusterCoordinate.x + 1.0) / inno_cluster_parameters.x * 2.0 - 1.0,
                    1.0 - clusterCoordinate.y / inno_cluster_parameters.y * 2.0);
                return center.x + projectedRadius.x >= tileMinimum.x
                    && center.x - projectedRadius.x <= tileMaximum.x
                    && center.y + projectedRadius.y >= tileMinimum.y
                    && center.y - projectedRadius.y <= tileMaximum.y;
            }

            NUM_THREADS(1, 1, 1)
            void main()
            {
                vec3 clusterCoordinate = vec3(gl_GlobalInvocationID.xyz);
                uint clusterIndex = gl_GlobalInvocationID.x
                    + gl_GlobalInvocationID.y * uint(inno_cluster_parameters.x)
                    + gl_GlobalInvocationID.z
                        * uint(inno_cluster_parameters.x * inno_cluster_parameters.y);
                uint maximumCount = uint(inno_cluster_depth_parameters.w);
                uint count = 0u;
                uint offset = clusterIndex * maximumCount;
                inno_cluster_grid[clusterIndex * 2u] = offset;
                {{ClusterLightTests()}}
                inno_cluster_grid[clusterIndex * 2u + 1u] = count;
            }
            """;
        var pass = new ShaderPassDefinition(
            "ClusterLights",
            BuiltinShaderPassTags.Compute,
            null,
            null,
            "Builtin/ClusterLights.cs.sc",
            null,
            GraphicsFeature.Compute | GraphicsFeature.StorageBuffer);
        var properties = new List<ShaderPropertyDefinition>
        {
                Property("inno_cluster_grid", "Cluster Grid", ShaderPropertyType.Buffer, ShaderStage.Compute, "null"),
                Property(
                    "inno_cluster_light_indices",
                    "Cluster Light Indices",
                    ShaderPropertyType.Buffer,
                    ShaderStage.Compute,
                    "null"),
                Property("inno_light_count", "Light Count", ShaderPropertyType.Vector4, ShaderStage.Compute),
                Property(
                    "inno_cluster_parameters",
                    "Cluster Dimensions",
                    ShaderPropertyType.Vector4,
                    ShaderStage.Compute),
                Property(
                    "inno_cluster_depth_parameters",
                    "Cluster Depth Range",
                    ShaderPropertyType.Vector4,
                    ShaderStage.Compute)
        };
        AddLocalLightProperties(properties);
        var definition = new ShaderDefinition(
            "Inno/Builtin/ClusterLights",
            properties,
            [],
            [pass]);
        return new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [Stage(ShaderStage.Compute, compute, "Builtin/ClusterLights.cs.sc", pass.name)])]);
    }

    private static string ClusterLightTests()
        => string.Join(
            Environment.NewLine,
            Enumerable.Range(0, C_MAX_LOCAL_LIGHTS).Select(index => $$"""
                if (inno_light_count.x > {{index}}.5
                    && count < maximumCount
                    && InnoIntersectsCluster(
                        inno_local_light_position_range_{{index}},
                        clusterCoordinate))
                {
                    inno_cluster_light_indices[offset + count] = {{index}}u;
                    ++count;
                }
                """));

    private static ShaderIRModule CreateDeferredLightingModule()
    {
        string fragment = $$"""
            $input v_texcoord0
            #include <bgfx_shader.sh>
            SAMPLER2D(inno_gbuffer_base_color_metallic, 0);
            SAMPLER2D(inno_gbuffer_normal_roughness, 1);
            SAMPLER2D(inno_gbuffer_emissive_occlusion, 2);
            SAMPLER2D(inno_scene_depth, 3);
            SAMPLER2DARRAY(inno_shadow_atlas, 4);
            uniform vec4 inno_main_light_direction;
            uniform vec4 inno_main_light_color;
            uniform vec4 inno_light_count;
            uniform vec4 inno_camera_position;
            uniform vec4 inno_view_parameters;
            uniform vec4 inno_shadow_cascade_splits;
            uniform vec4 inno_shadow_parameters;
            uniform mat4 inno_shadow_matrix_0;
            uniform mat4 inno_shadow_matrix_1;
            uniform mat4 inno_shadow_matrix_2;
            uniform mat4 inno_shadow_matrix_3;
            {{LocalLightDeclarations()}}

            {{PbrFunctions()}}

            void main()
            {
                vec4 baseMetallic = texture2D(inno_gbuffer_base_color_metallic, v_texcoord0);
                vec4 normalRoughness = texture2D(inno_gbuffer_normal_roughness, v_texcoord0);
                vec4 emissiveOcclusion = texture2D(inno_gbuffer_emissive_occlusion, v_texcoord0);
                float depth = texture2D(inno_scene_depth, v_texcoord0).x;
                vec3 normal = normalize(normalRoughness.xyz * 2.0 - 1.0);
                float metallic = clamp(baseMetallic.a, 0.0, 1.0);
                float roughness = clamp(normalRoughness.a, 0.04, 1.0);
                float ndcDepth = inno_view_parameters.z > 0.5 ? depth * 2.0 - 1.0 : depth;
                float ndcY = inno_view_parameters.w > 0.5
                    ? v_texcoord0.y * 2.0 - 1.0
                    : 1.0 - v_texcoord0.y * 2.0;
                vec4 worldHomogeneous = mul(
                    u_invViewProj,
                    vec4(v_texcoord0.x * 2.0 - 1.0, ndcY, ndcDepth, 1.0));
                vec3 worldPosition = worldHomogeneous.xyz / max(abs(worldHomogeneous.w), 0.00001);
                vec3 viewDirection = normalize(inno_camera_position.xyz - worldPosition);
                vec3 direct = vec3(0.0);
                if (inno_light_count.y > 0.5)
                {
                    direct += InnoEvaluatePbr(
                        baseMetallic.rgb,
                        metallic,
                        roughness,
                        normal,
                        viewDirection,
                        normalize(-inno_main_light_direction.xyz),
                        inno_main_light_color.rgb
                            * InnoEvaluateDirectionalShadow(worldPosition));
                }
                {{LocalLighting(
                    "direct",
                    "worldPosition",
                    "normal",
                    "viewDirection",
                    "baseMetallic.rgb",
                    "metallic",
                    "roughness")}}
                float interfaceKeep = inno_light_count.x + depth
                    + inno_view_parameters.x + inno_camera_position.x;
                {{LocalInterfaceKeep("interfaceKeep")}}
                if (interfaceKeep < -1.0) discard;
                gl_FragColor = vec4(direct * emissiveOcclusion.a + emissiveOcclusion.rgb, 1.0);
            }
            """;
        var properties = new List<ShaderPropertyDefinition>
        {
            TextureProperty("inno_gbuffer_base_color_metallic", "GBuffer BaseColor Metallic"),
            TextureProperty("inno_gbuffer_normal_roughness", "GBuffer Normal Roughness"),
            TextureProperty("inno_gbuffer_emissive_occlusion", "GBuffer Emissive Occlusion"),
            TextureProperty("inno_scene_depth", "Scene Depth"),
            Property("inno_main_light_direction", "Main Light Direction"),
            Property("inno_main_light_color", "Main Light Color"),
            Property("inno_light_count", "Light Count"),
            Property("inno_camera_position", "Camera Position"),
            Property("inno_view_parameters", "View Parameters"),
            Property(
                "inno_shadow_atlas",
                "Directional Shadow Atlas",
                ShaderPropertyType.Texture2DArray,
                ShaderStage.Fragment,
                "null"),
            Property("inno_shadow_cascade_splits", "Shadow Cascade Splits"),
            Property("inno_shadow_parameters", "Shadow Parameters"),
            Property(
                "inno_shadow_matrix_0",
                "Shadow Matrix 0",
                ShaderPropertyType.Matrix4x4,
                defaultValue: "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]"),
            Property(
                "inno_shadow_matrix_1",
                "Shadow Matrix 1",
                ShaderPropertyType.Matrix4x4,
                defaultValue: "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]"),
            Property(
                "inno_shadow_matrix_2",
                "Shadow Matrix 2",
                ShaderPropertyType.Matrix4x4,
                defaultValue: "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]"),
            Property(
                "inno_shadow_matrix_3",
                "Shadow Matrix 3",
                ShaderPropertyType.Matrix4x4,
                defaultValue: "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]")
        };
        AddLocalLightProperties(properties);
        return FullscreenModule(
            "Inno/Builtin/DeferredLighting",
            "DeferredLighting",
            fragment,
            properties);
    }

    private static void AddLocalLightProperties(ICollection<ShaderPropertyDefinition> properties)
    {
        for (int index = 0; index < C_MAX_LOCAL_LIGHTS; index++)
        {
            properties.Add(Property(
                $"inno_local_light_position_range_{index}",
                $"Local Light {index} Position and Range"));
            properties.Add(Property(
                $"inno_local_light_direction_outer_{index}",
                $"Local Light {index} Direction and Outer Cone"));
            properties.Add(Property(
                $"inno_local_light_color_inner_{index}",
                $"Local Light {index} Color and Inner Cone",
                defaultValue: "[0,0,0,-1]"));
        }
    }

    private static string LocalLightDeclarations()
        => string.Join(
            Environment.NewLine,
            Enumerable.Range(0, C_MAX_LOCAL_LIGHTS).Select(static index =>
                $"uniform vec4 inno_local_light_position_range_{index};{Environment.NewLine}"
                + $"uniform vec4 inno_local_light_direction_outer_{index};{Environment.NewLine}"
                + $"uniform vec4 inno_local_light_color_inner_{index};"));

    private static string LocalLighting(
        string accumulator,
        string worldPosition,
        string normal,
        string viewDirection,
        string baseColor,
        string metallic,
        string roughness)
        => string.Join(
            Environment.NewLine,
            Enumerable.Range(0, C_MAX_LOCAL_LIGHTS).Select(index => $$"""
                if (inno_light_count.x > {{index}}.5)
                {
                    {{accumulator}} += InnoEvaluateLocalLight(
                        {{worldPosition}},
                        {{normal}},
                        {{viewDirection}},
                        {{baseColor}},
                        {{metallic}},
                        {{roughness}},
                        inno_local_light_position_range_{{index}},
                        inno_local_light_direction_outer_{{index}},
                        inno_local_light_color_inner_{{index}});
                }
                """));

    private static string LocalInterfaceKeep(string accumulator)
        => string.Join(
            Environment.NewLine,
            Enumerable.Range(0, C_MAX_LOCAL_LIGHTS).Select(index =>
                $"{accumulator} += inno_local_light_position_range_{index}.x"
                + $" + inno_local_light_direction_outer_{index}.x"
                + $" + inno_local_light_color_inner_{index}.x;"));

    private static string PbrFunctions()
        => """
        float InnoDistributionGgx(float NoH, float roughness)
        {
            float alpha = roughness * roughness;
            float alphaSquared = alpha * alpha;
            float denominator = NoH * NoH * (alphaSquared - 1.0) + 1.0;
            return alphaSquared / max(3.14159265 * denominator * denominator, 0.00001);
        }

        float InnoGeometrySchlickGgx(float NoV, float roughness)
        {
            float radius = roughness + 1.0;
            float k = radius * radius * 0.125;
            return NoV / max(NoV * (1.0 - k) + k, 0.00001);
        }

        vec3 InnoFresnelSchlick(float cosine, vec3 f0)
        {
            return f0 + (1.0 - f0) * pow(1.0 - saturate(cosine), 5.0);
        }

        vec3 InnoEvaluatePbr(
            vec3 baseColor,
            float metallic,
            float roughness,
            vec3 normal,
            vec3 viewDirection,
            vec3 lightDirection,
            vec3 radiance)
        {
            vec3 halfDirection = normalize(viewDirection + lightDirection);
            float NoL = max(dot(normal, lightDirection), 0.0);
            float NoV = max(dot(normal, viewDirection), 0.0001);
            float NoH = max(dot(normal, halfDirection), 0.0);
            float VoH = max(dot(viewDirection, halfDirection), 0.0);
            vec3 f0 = mix(vec3(0.04), baseColor, metallic);
            vec3 fresnel = InnoFresnelSchlick(VoH, f0);
            float distribution = InnoDistributionGgx(NoH, roughness);
            float geometry = InnoGeometrySchlickGgx(NoV, roughness)
                * InnoGeometrySchlickGgx(NoL, roughness);
            vec3 specular = distribution * geometry * fresnel / max(4.0 * NoV * NoL, 0.0001);
            vec3 diffuseWeight = (vec3(1.0) - fresnel) * (1.0 - metallic);
            return (diffuseWeight * baseColor / 3.14159265 + specular) * radiance * NoL;
        }

        vec3 InnoEvaluateLocalLight(
            vec3 worldPosition,
            vec3 normal,
            vec3 viewDirection,
            vec3 baseColor,
            float metallic,
            float roughness,
            vec4 positionRange,
            vec4 directionOuter,
            vec4 colorInner)
        {
            vec3 toLight = positionRange.xyz - worldPosition;
            float distanceSquared = max(dot(toLight, toLight), 0.0001);
            vec3 lightDirection = toLight * inversesqrt(distanceSquared);
            float rangeSquared = max(positionRange.w * positionRange.w, 0.0001);
            float rangeFactor = saturate(1.0 - distanceSquared / rangeSquared);
            float attenuation = rangeFactor * rangeFactor / max(distanceSquared, 1.0);
            float spot = colorInner.w < 0.0
                ? 1.0
                : smoothstep(
                    directionOuter.w,
                    colorInner.w,
                    dot(normalize(directionOuter.xyz), -lightDirection));
            return InnoEvaluatePbr(
                baseColor,
                metallic,
                roughness,
                normal,
                viewDirection,
                lightDirection,
                colorInner.rgb * attenuation * spot);
        }

        float InnoEvaluateDirectionalShadow(vec3 worldPosition)
        {
            if (inno_shadow_parameters.x < 0.5 || inno_shadow_parameters.y <= 0.0)
            {
                return 1.0;
            }

            float viewDistance = abs(mul(u_view, vec4(worldPosition, 1.0)).z);
            float cascade = 0.0;
            mat4 worldToShadow = inno_shadow_matrix_0;
            if (viewDistance > inno_shadow_cascade_splits.x && inno_shadow_parameters.x > 1.5)
            {
                cascade = 1.0;
                worldToShadow = inno_shadow_matrix_1;
            }
            if (viewDistance > inno_shadow_cascade_splits.y && inno_shadow_parameters.x > 2.5)
            {
                cascade = 2.0;
                worldToShadow = inno_shadow_matrix_2;
            }
            if (viewDistance > inno_shadow_cascade_splits.z && inno_shadow_parameters.x > 3.5)
            {
                cascade = 3.0;
                worldToShadow = inno_shadow_matrix_3;
            }

            vec4 shadowPosition = mul(worldToShadow, vec4(worldPosition, 1.0));
            vec3 shadowNdc = shadowPosition.xyz / max(abs(shadowPosition.w), 0.00001);
            vec2 shadowUv = shadowNdc.xy * 0.5 + 0.5;
            if (inno_view_parameters.w < 0.5)
            {
                shadowUv.y = 1.0 - shadowUv.y;
            }

            float receiverDepth = inno_view_parameters.z > 0.5
                ? shadowNdc.z * 0.5 + 0.5
                : shadowNdc.z;
            if (shadowUv.x <= 0.0 || shadowUv.x >= 1.0
                || shadowUv.y <= 0.0 || shadowUv.y >= 1.0
                || receiverDepth <= 0.0 || receiverDepth >= 1.0)
            {
                return 1.0;
            }

            float visibility = 0.0;
            for (int y = -1; y <= 1; y += 2)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    vec2 offset = vec2(float(x), float(y)) * inno_shadow_parameters.w * 0.5;
                    float storedDepth = texture2DArray(
                        inno_shadow_atlas,
                        vec3(shadowUv + offset, cascade)).x;
                    visibility += receiverDepth - inno_shadow_parameters.z <= storedDepth ? 0.25 : 0.0;
                }
            }

            return mix(1.0, visibility, inno_shadow_parameters.y);
        }
        """;

    private static ShaderIRModule CreateSkyModule()
    {
        const string fragment = """
            $input v_texcoord0
            #include <bgfx_shader.sh>
            SAMPLER2D(inno_scene_depth, 0);

            void main()
            {
                float depth = texture2D(inno_scene_depth, v_texcoord0).x;
                if (depth < 0.99999) discard;
                float horizon = saturate(1.0 - abs(v_texcoord0.y * 2.0 - 1.0));
                vec3 zenith = vec3(0.025, 0.08, 0.20);
                vec3 horizonColor = vec3(0.34, 0.56, 0.82);
                gl_FragColor = vec4(mix(zenith, horizonColor, horizon * horizon), 1.0);
            }
            """;
        return FullscreenModule(
            "Inno/Builtin/Sky",
            "Sky",
            fragment,
            [TextureProperty("inno_scene_depth", "Scene Depth")]);
    }

    private static ShaderIRModule CreateBloomModule(
        string passName,
        string textureId,
        bool bloomDownsample)
    {
        string filter = bloomDownsample
            ? "float brightness = max(max(color.r, color.g), color.b);\n    color *= smoothstep(1.0, 2.0, brightness);"
            : "color *= 0.85;";
        string fragment = $$"""
            $input v_texcoord0
            #include <bgfx_shader.sh>
            SAMPLER2D({{textureId}}, 0);

            void main()
            {
                vec3 color = texture2D({{textureId}}, v_texcoord0).rgb;
                {{filter}}
                gl_FragColor = vec4(color, 1.0);
            }
            """;
        return FullscreenModule(
            $"Inno/Builtin/{passName}",
            passName,
            fragment,
            [TextureProperty(textureId, passName + " Input")]);
    }

    private static ShaderIRModule FullscreenModule(
        string shaderName,
        string passName,
        string fragment,
        IReadOnlyList<ShaderPropertyDefinition> properties)
    {
        const string varying = "vec2 v_texcoord0 : TEXCOORD0;";
        const string vertex = """
            $output v_texcoord0
            #include <bgfx_shader.sh>

            void main()
            {
                float x = -1.0 + float((gl_VertexID & 1) << 2);
                float y =  1.0 - float((gl_VertexID & 2) << 1);
                gl_Position = vec4(x, y, 0.0, 1.0);
                v_texcoord0 = vec2((x + 1.0) * 0.5, (1.0 - y) * 0.5);
            }
            """;
        var pass = new ShaderPassDefinition(
            passName,
            BuiltinShaderPassTags.Fullscreen,
            $"Builtin/{passName}.vs.sc",
            $"Builtin/{passName}.fs.sc",
            null,
            $"Builtin/{passName}.varying.def.sc",
            renderState: new ShaderRenderState
            {
                cull = ShaderCullMode.None,
                depthCompare = ShaderCompareFunction.Always,
                depthWrite = false
            });
        var definition = new ShaderDefinition(shaderName, properties, [], [pass]);
        return new ShaderIRModule(
            definition,
            [new ShaderIRPass(
                pass,
                [
                    Stage(ShaderStage.Vertex, vertex, $"Builtin/{passName}.vs.sc", passName),
                    Stage(ShaderStage.Fragment, fragment, $"Builtin/{passName}.fs.sc", passName)
                ],
                varying)]);
    }

    private static ShaderPropertyDefinition TextureProperty(string id, string displayName)
        => Property(id, displayName, ShaderPropertyType.Texture2D, ShaderStage.Fragment, "null");

    private static ShaderPropertyDefinition Property(
        string id,
        string displayName,
        ShaderPropertyType type = ShaderPropertyType.Vector4,
        ShaderStage stage = ShaderStage.Fragment,
        string defaultValue = "[0,0,0,0]")
        => new(new ShaderPropertyId(id), displayName, type, stage, defaultValue);

    private static ShaderIRStageModule Stage(
        ShaderStage stage,
        string source,
        string assetPath,
        string passName)
        => new(
            stage,
            "main",
            source,
            ShaderIRSourceKind.Handwritten,
            new ShaderSourceLocation(assetPath, passName, stage));

    private static CompiledShaderPass RequirePass(CompiledShaderArtifact artifact, string name)
        => artifact.passes.FirstOrDefault(pass => string.Equals(pass.definition.name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Built-in artifact '{artifact.shaderName}' has no pass '{name}'.");

    private static ShaderStageArtifact RequireStage(CompiledShaderPass pass, ShaderStage stage)
        => pass.stages.FirstOrDefault(value => value.stage == stage)
            ?? throw new InvalidOperationException($"Built-in pass '{pass.definition.name}' has no '{stage}' stage.");
}
