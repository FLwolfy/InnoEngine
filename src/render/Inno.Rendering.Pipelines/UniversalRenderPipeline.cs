using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Mathematics;
using Inno.Rendering.Core;

namespace Inno.Rendering.Pipelines;

/// <summary>
/// Builds capability-aware Forward+ and Deferred frame graphs over a shared material contract.
/// </summary>
[RenderPipelineExtension("inno.pipeline.universal")]
public sealed class UniversalRenderPipeline : RenderPipeline
{
    private const int C_CLUSTER_TILE_SIZE = 16;
    private const int C_CLUSTER_DEPTH_SLICES = 24;
    private const int C_MAX_CLUSTER_LIGHTS = 8;

    private static readonly RenderBindingId S_SCENE_COLOR = new("inno_scene_color");
    private static readonly RenderBindingId S_SCENE_DEPTH = new("inno_scene_depth");
    private static readonly RenderBindingId S_SHADOW_ATLAS = new("inno_shadow_atlas");
    private static readonly RenderBindingId S_GBUFFER0 = new("inno_gbuffer_base_color_metallic");
    private static readonly RenderBindingId S_GBUFFER1 = new("inno_gbuffer_normal_roughness");
    private static readonly RenderBindingId S_GBUFFER2 = new("inno_gbuffer_emissive_occlusion");
    private static readonly RenderBindingId S_CLUSTER_GRID = new("inno_cluster_grid");
    private static readonly RenderBindingId S_CLUSTER_LIGHT_INDICES = new("inno_cluster_light_indices");
    private static readonly RenderBindingId S_CLUSTER_PARAMETERS = new("inno_cluster_parameters");
    private static readonly RenderBindingId S_CLUSTER_DEPTH_PARAMETERS = new(
        "inno_cluster_depth_parameters");
    private static readonly RenderBindingId S_BLOOM = new("inno_bloom");

    /// <inheritdoc />
    public override void Build(RenderPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.request.target.kind == RenderTargetKind.Texture)
        {
            RenderTexture target = context.request.target.texture
                ?? throw new InvalidOperationException("A texture render target requires a RenderTexture instance.");
            context.resources.PublishCameraTarget(context.executor.ImportTarget(context.graph, target));
        }

        RenderCullingResults culling = context.world.Cull(context.request.view);
        RenderPath path = ResolvePath(context);
        RenderTextureFormat sceneFormat = SelectSceneColorFormat(context);
        RenderTextureFormat depthFormat = SelectDepthFormat(context);
        RenderTextureHandle sceneColor = context.graph.CreateTexture(
            Name(context, "Scene Color"),
            ColorDescriptor(context, sceneFormat));
        RenderTextureHandle sceneDepth = context.graph.CreateTexture(
            Name(context, "Scene Depth"),
            DepthDescriptor(context, depthFormat));
        context.resources.PublishSceneColor(sceneColor);
        context.resources.PublishSceneDepth(sceneDepth);

        ShadowBuildResult shadows = BuildDirectionalShadows(context, culling, depthFormat);
        ClusterBuildResult clusters = path == RenderPath.ForwardPlus || culling.transparentObjects.Count != 0
            ? BuildLightClusters(context, culling)
            : default;

        if (path == RenderPath.Deferred)
        {
            BuildDeferred(
                context,
                culling,
                sceneColor,
                sceneDepth,
                shadows.atlas,
                shadows.data);
        }
        else
        {
            BuildForward(
                context,
                culling,
                sceneColor,
                sceneDepth,
                shadows.atlas,
                shadows.data,
                clusters);
        }

        BuildSky(context, sceneColor, sceneDepth);
        BuildTransparent(
            context,
            culling,
            sceneColor,
            sceneDepth,
            shadows.atlas,
            shadows.data,
            clusters);
        BuildPicking(context, culling, sceneDepth);
        BuildPostProcessing(context, sceneColor);
    }

    /// <summary>
    /// Resolves the requested path against current device capabilities.
    /// </summary>
    /// <param name="requestedPath">Camera or pipeline path request.</param>
    /// <param name="capabilities">Current device capabilities.</param>
    /// <returns>The executable path; Deferred falls back to Forward+ when unavailable.</returns>
    public static RenderPath ResolvePath(RenderPath requestedPath, GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        RenderPath normalized = requestedPath == RenderPath.Automatic
            ? RenderPath.ForwardPlus
            : requestedPath;
        return normalized == RenderPath.Deferred && !SupportsDeferred(capabilities)
            ? RenderPath.ForwardPlus
            : normalized;
    }

    /// <summary>
    /// Tests whether the fixed semantic GBuffer can be represented on a device.
    /// </summary>
    /// <param name="capabilities">Current device capabilities.</param>
    /// <returns><see langword="true"/> when three compatible color attachments and depth are available.</returns>
    public static bool SupportsDeferred(GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.limits.maxColorAttachments >= 3
            && capabilities.SupportsRenderTarget(RenderTextureFormat.RGBA8)
            && (capabilities.SupportsRenderTarget(RenderTextureFormat.RGB10A2)
                || capabilities.SupportsRenderTarget(RenderTextureFormat.RGBA8))
            && SupportsAnyDepth(capabilities);
    }

    private static RenderPath ResolvePath(RenderPipelineContext context)
    {
        RenderPath requested = context.resolvedPath;
        if (requested == RenderPath.Automatic)
        {
            requested = context.request.renderPath == RenderPath.Automatic
                ? context.pipelineAsset.defaultRenderPath
                : context.request.renderPath;
        }

        RenderPath resolved = ResolvePath(requested, context.capabilities);
        if (requested == RenderPath.Deferred && resolved != requested)
        {
            context.diagnostics.Publish(new RenderDiagnostic(
                "RENDER_PIPELINE_DEFERRED_FALLBACK",
                "Deferred requires three compatible GBuffer attachments and depth support; this view is using Forward+.",
                RenderDiagnosticSeverity.Warning,
                context.request.name));
        }

        return resolved;
    }

    private static RenderTextureFormat SelectSceneColorFormat(RenderPipelineContext context)
    {
        if (!context.pipelineAsset.quality.hdr)
        {
            return RenderTextureFormat.RGBA8;
        }

        if (context.capabilities.SupportsRenderTarget(RenderTextureFormat.RGBA16Float))
        {
            return RenderTextureFormat.RGBA16Float;
        }

        if (context.capabilities.SupportsRenderTarget(RenderTextureFormat.RG11B10Float))
        {
            context.diagnostics.Publish(new RenderDiagnostic(
                "RENDER_PIPELINE_HDR_FORMAT_REDUCED",
                "RGBA16F render targets are unavailable; HDR is using RG11B10F without alpha.",
                RenderDiagnosticSeverity.Warning,
                context.request.name));
            return RenderTextureFormat.RG11B10Float;
        }

        context.diagnostics.Publish(new RenderDiagnostic(
            "RENDER_PIPELINE_HDR_DISABLED",
            "Floating-point render targets are unavailable; this view is using linear RGBA8.",
            RenderDiagnosticSeverity.Warning,
            context.request.name));
        return RenderTextureFormat.RGBA8;
    }

    private static RenderTextureFormat SelectDepthFormat(RenderPipelineContext context)
    {
        if (context.capabilities.SupportsRenderTarget(RenderTextureFormat.Depth24Stencil8))
        {
            return RenderTextureFormat.Depth24Stencil8;
        }

        if (context.capabilities.SupportsRenderTarget(RenderTextureFormat.Depth32Float))
        {
            return RenderTextureFormat.Depth32Float;
        }

        throw new InvalidOperationException("The active graphics device exposes no supported depth attachment format.");
    }

    private static ShadowBuildResult BuildDirectionalShadows(
        RenderPipelineContext context,
        RenderCullingResults culling,
        RenderTextureFormat depthFormat)
    {
        RenderLightData? shadowLight = culling.lights.FirstOrDefault(
            static light => light.kind == RenderLightKind.Directional && light.castsShadows);
        if (shadowLight is null || culling.shadowCasters.Count == 0)
        {
            return new ShadowBuildResult(default, null);
        }

        int cascades = Math.Min(
            context.pipelineAsset.quality.directionalShadowCascades,
            shadowLight.shadowCascadeCount);
        int resolution = Math.Min(
            context.pipelineAsset.quality.shadowResolution,
            context.capabilities.limits.maxTextureSize);
        RenderTextureHandle atlas = context.graph.CreateTexture(
            Name(context, "Directional Shadow Cascades"),
            new RenderTextureDescriptor(
                resolution,
                resolution,
                depthFormat,
                RenderTextureUsage.DepthStencilAttachment | RenderTextureUsage.Sampled,
                arrayLayers: cascades));
        context.resources.PublishShadowAtlas(atlas);
        var matrices = new List<Matrix>(cascades);
        var splits = new List<float>(cascades);
        (float cameraNear, float cameraFar) = ProjectionRange(context.request.view.projectionMatrix);

        for (int cascade = 0; cascade < cascades; cascade++)
        {
            RenderView shadowView = CreateDirectionalShadowView(
                context.request.view,
                shadowLight.direction,
                cascade,
                cascades,
                resolution);
            Matrix correctedProjection = CorrectProjection(
                shadowView.projectionMatrix,
                context.capabilities.homogeneousDepth);
            matrices.Add(correctedProjection * shadowView.viewMatrix);
            splits.Add(CascadeDistance(
                cameraNear,
                cameraFar,
                (cascade + 1f) / cascades));
            RenderPipelineOperation operation = new(
                BuiltinPipelineOperations.DirectionalShadow,
                RenderPipelineOperationKind.Scene,
                shadowView,
                BuiltinShaderPassTags.ShadowCaster,
                culling.shadowCasters,
                culling.lights,
                subpassIndex: cascade);
            RasterPassBuilder pass = context.graph.AddRasterPass(
                Name(context, $"Directional Shadow {cascade}"),
                BuiltinRenderPhases.shadows,
                new PassExecution(context.executor, operation),
                Execute);
            Prepare(context, operation, pass);
            pass.UseDepthAttachment(
                atlas,
                RenderLoadAction.Clear,
                clearDepth: 1f,
                arrayLayer: cascade);
        }

        return new ShadowBuildResult(
            atlas,
            new DirectionalShadowData(
                matrices,
                splits,
                shadowLight.shadowStrength,
                1.5f / resolution,
                1f / resolution));
    }

    private static ClusterBuildResult BuildLightClusters(
        RenderPipelineContext context,
        RenderCullingResults culling)
    {
        bool clustered = context.capabilities.Supports(
                GraphicsFeature.Compute | GraphicsFeature.StorageBuffer)
            && context.capabilities.limits.maxComputeBindings >= 2;
        if (!clustered)
        {
            context.diagnostics.Publish(new RenderDiagnostic(
                "RENDER_PIPELINE_FORWARD_CPU_LIGHTS",
                "Compute or storage buffers are unavailable; local lights are using the CPU forward-light list.",
                RenderDiagnosticSeverity.Warning,
                context.request.name));
            return default;
        }

        int localLightCount = Math.Min(
            C_MAX_CLUSTER_LIGHTS,
            culling.lights.Count(static light => light.kind != RenderLightKind.Directional));
        if (localLightCount == 0)
        {
            return default;
        }

        int tilesX = DivideRoundUp(context.request.view.pixelWidth, C_CLUSTER_TILE_SIZE);
        int tilesY = DivideRoundUp(context.request.view.pixelHeight, C_CLUSTER_TILE_SIZE);
        int clusterCount = checked(tilesX * tilesY * C_CLUSTER_DEPTH_SLICES);
        int lightIndexCount = checked(clusterCount * C_MAX_CLUSTER_LIGHTS);
        RenderBufferHandle grid = context.graph.CreateBuffer(
            Name(context, "Cluster Grid"),
            new RenderBufferDescriptor(clusterCount, sizeof(uint) * 2, RenderBufferUsage.Storage));
        RenderBufferHandle indices = context.graph.CreateBuffer(
            Name(context, "Cluster Light Indices"),
            new RenderBufferDescriptor(lightIndexCount, sizeof(uint), RenderBufferUsage.Storage));
        IReadOnlyList<RenderUniformBinding> uniforms = ClusterUniforms(
            context.request.view,
            tilesX,
            tilesY);
        RenderPipelineOperation operation = new(
            BuiltinPipelineOperations.ClusterLights,
            RenderPipelineOperationKind.Compute,
            context.request.view,
            lights: culling.lights,
            buffers:
            [
                new RenderBufferBinding(S_CLUSTER_GRID, grid),
                new RenderBufferBinding(S_CLUSTER_LIGHT_INDICES, indices)
            ],
            dispatchX: tilesX,
            dispatchY: tilesY,
            dispatchZ: C_CLUSTER_DEPTH_SLICES,
            uniforms: uniforms);
        ComputePassBuilder pass = context.graph.AddComputePass(
            Name(context, "Cluster Lights"),
            BuiltinRenderPhases.beforeRendering,
            new PassExecution(context.executor, operation),
            Execute);
        context.executor.Prepare(operation);
        Matrix projection = CorrectProjection(
            context.request.view.projectionMatrix,
            context.capabilities.homogeneousDepth);
        pass.SetViewTransform(
            context.request.view.viewMatrix.ToColumnMajorArray(),
            projection.ToColumnMajorArray());
        pass.WriteBuffer(grid);
        pass.WriteBuffer(indices);
        return new ClusterBuildResult(grid, indices, uniforms);
    }

    private static void BuildForward(
        RenderPipelineContext context,
        RenderCullingResults culling,
        RenderTextureHandle sceneColor,
        RenderTextureHandle sceneDepth,
        RenderTextureHandle shadowAtlas,
        DirectionalShadowData? shadowData,
        ClusterBuildResult clusters)
    {
        RenderPipelineOperation operation = new(
            BuiltinPipelineOperations.ForwardOpaque,
            RenderPipelineOperationKind.Scene,
            context.request.view,
            clusters.isValid
                ? BuiltinShaderPassTags.ForwardLitClustered
                : BuiltinShaderPassTags.ForwardLit,
            culling.opaqueObjects,
            culling.lights,
            TextureBindings(shadowAtlas),
            buffers: BufferBindings(clusters),
            directionalShadow: shadowData,
            uniforms: clusters.uniforms);
        RasterPassBuilder pass = context.graph.AddRasterPass(
            Name(context, "Forward Opaque"),
            BuiltinRenderPhases.opaque,
            new PassExecution(context.executor, operation),
            Execute);
        Prepare(context, operation, pass);
        pass.UseColorAttachment(
            sceneColor,
            0,
            RenderLoadAction.Clear,
            clearColor: ToClearColor(context.request.backgroundColor));
        pass.UseDepthAttachment(sceneDepth, RenderLoadAction.Clear);
        ReadIfValid(pass, shadowAtlas);
        ReadIfValid(pass, clusters.grid);
        ReadIfValid(pass, clusters.indices);
    }

    private static void BuildDeferred(
        RenderPipelineContext context,
        RenderCullingResults culling,
        RenderTextureHandle sceneColor,
        RenderTextureHandle sceneDepth,
        RenderTextureHandle shadowAtlas,
        DirectionalShadowData? shadowData)
    {
        RenderTextureHandle gBuffer0 = context.graph.CreateTexture(
            Name(context, "GBuffer BaseColor Metallic"),
            ColorDescriptor(context, RenderTextureFormat.RGBA8));
        RenderTextureHandle gBuffer1 = context.graph.CreateTexture(
            Name(context, "GBuffer Normal Roughness"),
            ColorDescriptor(
                context,
                context.capabilities.SupportsRenderTarget(RenderTextureFormat.RGB10A2)
                    ? RenderTextureFormat.RGB10A2
                    : RenderTextureFormat.RGBA8));
        RenderTextureHandle gBuffer2 = context.graph.CreateTexture(
            Name(context, "GBuffer Emissive Occlusion"),
            ColorDescriptor(
                context,
                context.capabilities.SupportsRenderTarget(RenderTextureFormat.RGBA16Float)
                    ? RenderTextureFormat.RGBA16Float
                    : RenderTextureFormat.RGBA8));
        context.resources.PublishGBuffer(gBuffer0, gBuffer1, gBuffer2);

        RenderPipelineOperation geometryOperation = new(
            BuiltinPipelineOperations.GBuffer,
            RenderPipelineOperationKind.Scene,
            context.request.view,
            BuiltinShaderPassTags.GBuffer,
            culling.opaqueObjects,
            culling.lights);
        RasterPassBuilder geometry = context.graph.AddRasterPass(
            Name(context, "Deferred GBuffer"),
            BuiltinRenderPhases.opaque,
            new PassExecution(context.executor, geometryOperation),
            Execute);
        Prepare(context, geometryOperation, geometry);
        geometry.UseColorAttachment(gBuffer0, 0, RenderLoadAction.Clear);
        geometry.UseColorAttachment(gBuffer1, 1, RenderLoadAction.Clear);
        geometry.UseColorAttachment(gBuffer2, 2, RenderLoadAction.Clear);
        geometry.UseDepthAttachment(sceneDepth, RenderLoadAction.Clear);

        List<RenderTextureBinding> textures =
        [
            new RenderTextureBinding(S_GBUFFER0, gBuffer0),
            new RenderTextureBinding(S_GBUFFER1, gBuffer1),
            new RenderTextureBinding(S_GBUFFER2, gBuffer2),
            new RenderTextureBinding(S_SCENE_DEPTH, sceneDepth)
        ];
        if (shadowAtlas.isValid)
        {
            textures.Add(new RenderTextureBinding(S_SHADOW_ATLAS, shadowAtlas));
        }

        RenderPipelineOperation lightingOperation = new(
            BuiltinPipelineOperations.DeferredLighting,
            RenderPipelineOperationKind.Fullscreen,
            context.request.view,
            lights: culling.lights,
            textures: textures,
            directionalShadow: shadowData);
        RasterPassBuilder lighting = context.graph.AddRasterPass(
            Name(context, "Deferred Lighting"),
            BuiltinRenderPhases.lighting,
            new PassExecution(context.executor, lightingOperation),
            Execute);
        Prepare(context, lightingOperation, lighting);
        lighting.UseColorAttachment(
            sceneColor,
            0,
            RenderLoadAction.Clear,
            clearColor: ToClearColor(context.request.backgroundColor));
        lighting.ReadTexture(gBuffer0);
        lighting.ReadTexture(gBuffer1);
        lighting.ReadTexture(gBuffer2);
        lighting.ReadTexture(sceneDepth);
        ReadIfValid(lighting, shadowAtlas);
    }

    private static void BuildSky(
        RenderPipelineContext context,
        RenderTextureHandle sceneColor,
        RenderTextureHandle sceneDepth)
    {
        if (context.request.clearMode != CameraClearMode.Sky)
        {
            return;
        }

        RenderPipelineOperation operation = new(
            BuiltinPipelineOperations.Sky,
            RenderPipelineOperationKind.Fullscreen,
            context.request.view,
            textures: [new RenderTextureBinding(S_SCENE_DEPTH, sceneDepth)]);
        RasterPassBuilder pass = context.graph.AddRasterPass(
            Name(context, "Sky"),
            BuiltinRenderPhases.lighting,
            new PassExecution(context.executor, operation),
            Execute);
        Prepare(context, operation, pass);
        pass.UseColorAttachment(sceneColor, 0, RenderLoadAction.Load);
        pass.UseDepthAttachment(sceneDepth, RenderLoadAction.Load);
    }

    private static void BuildTransparent(
        RenderPipelineContext context,
        RenderCullingResults culling,
        RenderTextureHandle sceneColor,
        RenderTextureHandle sceneDepth,
        RenderTextureHandle shadowAtlas,
        DirectionalShadowData? shadowData,
        ClusterBuildResult clusters)
    {
        RenderPipelineOperation operation = new(
            BuiltinPipelineOperations.Transparent,
            RenderPipelineOperationKind.Scene,
            context.request.view,
            clusters.isValid
                ? BuiltinShaderPassTags.ForwardLitClustered
                : BuiltinShaderPassTags.ForwardLit,
            culling.transparentObjects,
            culling.lights,
            TextureBindings(shadowAtlas),
            buffers: BufferBindings(clusters),
            directionalShadow: shadowData,
            uniforms: clusters.uniforms);
        RasterPassBuilder pass = context.graph.AddRasterPass(
            Name(context, "Transparent"),
            BuiltinRenderPhases.transparent,
            new PassExecution(context.executor, operation),
            Execute);
        Prepare(context, operation, pass);
        pass.UseColorAttachment(sceneColor, 0, RenderLoadAction.Load);
        pass.UseDepthAttachment(sceneDepth, RenderLoadAction.Load);
        ReadIfValid(pass, shadowAtlas);
        ReadIfValid(pass, clusters.grid);
        ReadIfValid(pass, clusters.indices);
    }

    private static void BuildPostProcessing(
        RenderPipelineContext context,
        RenderTextureHandle sceneColor)
    {
        RenderTextureHandle bloom = default;
        if (context.pipelineAsset.quality.bloom
            && context.request.view.pixelWidth >= 2
            && context.request.view.pixelHeight >= 2)
        {
            RenderTextureFormat bloomFormat = SelectSceneColorFormat(context);
            RenderTextureDescriptor bloomDescriptor = new(
                Math.Max(1, context.request.view.pixelWidth / 2),
                Math.Max(1, context.request.view.pixelHeight / 2),
                bloomFormat,
                RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled);
            RenderTextureHandle bloomDown = context.graph.CreateTexture(
                Name(context, "Bloom Downsample"),
                bloomDescriptor);
            bloom = context.graph.CreateTexture(Name(context, "Bloom Upsample"), bloomDescriptor);

            RenderPipelineOperation downOperation = new(
                BuiltinPipelineOperations.BloomDownsample,
                RenderPipelineOperationKind.Fullscreen,
                context.request.view,
                textures: [new RenderTextureBinding(S_SCENE_COLOR, sceneColor)]);
            RasterPassBuilder down = context.graph.AddRasterPass(
                Name(context, "Bloom Downsample"),
                BuiltinRenderPhases.postProcessing,
                new PassExecution(context.executor, downOperation),
                Execute);
            Prepare(context, downOperation, down);
            down.UseColorAttachment(bloomDown, 0, RenderLoadAction.Discard);
            down.ReadTexture(sceneColor);

            RenderPipelineOperation upOperation = new(
                BuiltinPipelineOperations.BloomUpsample,
                RenderPipelineOperationKind.Fullscreen,
                context.request.view,
                textures: [new RenderTextureBinding(S_BLOOM, bloomDown)]);
            RasterPassBuilder up = context.graph.AddRasterPass(
                Name(context, "Bloom Upsample"),
                BuiltinRenderPhases.postProcessing,
                new PassExecution(context.executor, upOperation),
                Execute);
            Prepare(context, upOperation, up);
            up.UseColorAttachment(bloom, 0, RenderLoadAction.Discard);
            up.ReadTexture(bloomDown);
        }

        List<RenderTextureBinding> toneTextures = [new RenderTextureBinding(S_SCENE_COLOR, sceneColor)];
        if (bloom.isValid)
        {
            toneTextures.Add(new RenderTextureBinding(S_BLOOM, bloom));
        }

        RenderPipelineOperation toneOperation = new(
            bloom.isValid
                ? BuiltinPipelineOperations.ToneMapBloom
                : BuiltinPipelineOperations.ToneMap,
            RenderPipelineOperationKind.Fullscreen,
            context.request.view,
            textures: toneTextures,
            scalarParameter: context.pipelineAsset.quality.exposure);
        RasterPassBuilder tone = context.graph.AddRasterPass(
            Name(context, "Tone Mapping"),
            BuiltinRenderPhases.afterRendering,
            new PassExecution(context.executor, toneOperation),
            Execute);
        Prepare(context, toneOperation, tone);
        tone.ReadTexture(sceneColor);
        ReadIfValid(tone, bloom);
        if (context.resources.cameraTarget.isValid)
        {
            tone.UseColorAttachment(context.resources.cameraTarget, 0, RenderLoadAction.Discard);
        }
        else
        {
            tone.HasSideEffect();
        }
    }

    private static void BuildPicking(
        RenderPipelineContext context,
        RenderCullingResults culling,
        RenderTextureHandle sceneDepth)
    {
        if (!context.request.enablePicking)
        {
            return;
        }

        RenderTextureHandle picking = context.graph.CreateTexture(
            Name(context, "Picking"),
            ColorDescriptor(context, RenderTextureFormat.RGBA8));
        context.resources.PublishPicking(picking);
        RenderObjectData[] objects = culling.opaqueObjects.Concat(culling.transparentObjects).ToArray();
        RenderPipelineOperation operation = new(
            BuiltinPipelineOperations.Picking,
            RenderPipelineOperationKind.Scene,
            context.request.view,
            BuiltinShaderPassTags.Picking,
            objects);
        RasterPassBuilder pass = context.graph.AddRasterPass(
            Name(context, "Picking"),
            BuiltinRenderPhases.editorOverlay,
            new PassExecution(context.executor, operation),
            Execute);
        Prepare(context, operation, pass);
        pass.UseColorAttachment(picking, 0, RenderLoadAction.Clear);
        pass.UseDepthAttachment(sceneDepth, RenderLoadAction.Load);
        pass.HasSideEffect();
    }

    private static IReadOnlyList<RenderTextureBinding> TextureBindings(RenderTextureHandle shadowAtlas)
        => shadowAtlas.isValid
            ? [new RenderTextureBinding(S_SHADOW_ATLAS, shadowAtlas)]
            : Array.Empty<RenderTextureBinding>();

    private static IReadOnlyList<RenderBufferBinding> BufferBindings(ClusterBuildResult clusters)
        => clusters.isValid
            ?
            [
                new RenderBufferBinding(S_CLUSTER_GRID, clusters.grid),
                new RenderBufferBinding(S_CLUSTER_LIGHT_INDICES, clusters.indices)
            ]
            : Array.Empty<RenderBufferBinding>();

    private static IReadOnlyList<RenderUniformBinding> ClusterUniforms(
        RenderView view,
        int tilesX,
        int tilesY)
    {
        (float near, float far) = ProjectionRange(view.projectionMatrix);
        float logRange = MathF.Log(Math.Max(far / Math.Max(near, 0.0001f), 1.0001f));
        return
        [
            new RenderUniformBinding(
                S_CLUSTER_PARAMETERS,
                new Vector4(tilesX, tilesY, C_CLUSTER_DEPTH_SLICES, C_CLUSTER_TILE_SIZE)),
            new RenderUniformBinding(
                S_CLUSTER_DEPTH_PARAMETERS,
                new Vector4(near, far, logRange, C_MAX_CLUSTER_LIGHTS))
        ];
    }

    private static RenderTextureDescriptor ColorDescriptor(
        RenderPipelineContext context,
        RenderTextureFormat format)
        => new(
            context.request.view.pixelWidth,
            context.request.view.pixelHeight,
            format,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled);

    private static RenderTextureDescriptor DepthDescriptor(
        RenderPipelineContext context,
        RenderTextureFormat format)
        => new(
            context.request.view.pixelWidth,
            context.request.view.pixelHeight,
            format,
            RenderTextureUsage.DepthStencilAttachment | RenderTextureUsage.Sampled);

    private static bool SupportsAnyDepth(GraphicsCapabilities capabilities)
        => capabilities.SupportsRenderTarget(RenderTextureFormat.Depth24Stencil8)
            || capabilities.SupportsRenderTarget(RenderTextureFormat.Depth32Float);

    private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;

    private static string Name(RenderPipelineContext context, string suffix)
        => $"{context.request.name}/{suffix}";

    private static RenderClearColor ToClearColor(Inno.Core.Mathematics.Color color)
        => new(color.r, color.g, color.b, color.a);

    private static void ReadIfValid(RenderPassBuilder pass, RenderTextureHandle texture)
    {
        if (texture.isValid)
        {
            pass.ReadTexture(texture);
        }
    }

    private static void ReadIfValid(RenderPassBuilder pass, RenderBufferHandle buffer)
    {
        if (buffer.isValid)
        {
            pass.ReadBuffer(buffer);
        }
    }

    private static void Execute(PassExecution execution, RenderPassContext context)
        => execution.executor.Execute(execution.operation, context);

    private static void Prepare(
        RenderPipelineContext context,
        RenderPipelineOperation operation,
        RasterPassBuilder pass)
    {
        context.executor.Prepare(operation);
        Matrix projection = CorrectProjection(
            operation.view.projectionMatrix,
            context.capabilities.homogeneousDepth);

        pass.SetViewTransform(
            operation.view.viewMatrix.ToColumnMajorArray(),
            projection.ToColumnMajorArray());
    }

    private static Matrix CorrectProjection(Matrix projection, bool homogeneousDepth)
    {
        if (!homogeneousDepth)
        {
            return projection;
        }

        Matrix depthCorrection = Matrix.identity;
        depthCorrection.m33 = 2f;
        depthCorrection.m34 = -1f;
        return depthCorrection * projection;
    }

    private static RenderView CreateDirectionalShadowView(
        RenderView camera,
        Vector3 lightDirection,
        int cascadeIndex,
        int cascadeCount,
        int resolution)
    {
        Matrix viewProjection = camera.projectionMatrix * camera.viewMatrix;
        Matrix inverseViewProjection = Matrix.Invert(viewProjection);
        Vector3[] nearCorners = FrustumPlaneCorners(inverseViewProjection, 0f);
        Vector3[] farCorners = FrustumPlaneCorners(inverseViewProjection, 1f);
        (float near, float far) = ProjectionRange(camera.projectionMatrix);
        float previousDistance = cascadeIndex == 0
            ? near
            : CascadeDistance(near, far, cascadeIndex / (float)cascadeCount);
        float currentDistance = CascadeDistance(near, far, (cascadeIndex + 1f) / cascadeCount);
        float previousT = (previousDistance - near) / (far - near);
        float currentT = (currentDistance - near) / (far - near);
        Vector3[] corners = new Vector3[8];
        Vector3 center = Vector3.ZERO;
        for (int index = 0; index < 4; index++)
        {
            corners[index] = Vector3.Lerp(nearCorners[index], farCorners[index], previousT);
            corners[index + 4] = Vector3.Lerp(nearCorners[index], farCorners[index], currentT);
            center += corners[index] + corners[index + 4];
        }

        center /= corners.Length;
        float radius = corners.Max(corner => (corner - center).Length());
        radius = MathF.Ceiling(radius * 16f) / 16f;
        Vector3 direction = lightDirection.normalized;
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UP)) > 0.95f
            ? Vector3.RIGHT
            : Vector3.UP;
        Vector3 eye = center - direction * (radius * 2f);
        Matrix lightView = Matrix.CreateLookAt(eye, center, up);
        Vector3[] lightCorners = corners.Select(corner => Vector3.Transform(corner, lightView)).ToArray();
        float minX = lightCorners.Min(static corner => corner.x);
        float maxX = lightCorners.Max(static corner => corner.x);
        float minY = lightCorners.Min(static corner => corner.y);
        float maxY = lightCorners.Max(static corner => corner.y);
        float minZ = lightCorners.Min(static corner => corner.z);
        float maxZ = lightCorners.Max(static corner => corner.z);
        float extent = MathF.Max(maxX - minX, maxY - minY);
        float texelSize = extent / resolution;
        float centerX = MathF.Floor(((minX + maxX) * 0.5f) / texelSize) * texelSize;
        float centerY = MathF.Floor(((minY + maxY) * 0.5f) / texelSize) * texelSize;
        float halfExtent = extent * 0.5f;
        Matrix projection = Matrix.CreateOrthographicOffCenter(
            centerX - halfExtent,
            centerX + halfExtent,
            centerY - halfExtent,
            centerY + halfExtent,
            MathF.Max(0.001f, minZ - radius),
            maxZ + radius);
        return new RenderView(
            lightView,
            projection,
            eye,
            resolution,
            resolution,
            camera.cullingMask);
    }

    private static Vector3[] FrustumPlaneCorners(Matrix inverseViewProjection, float depth)
    {
        Vector3[] corners = new Vector3[4];
        int index = 0;
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                Vector4 homogeneous = Vector4.Transform(
                    new Vector4(x == 0 ? -1f : 1f, y == 0 ? -1f : 1f, depth, 1f),
                    inverseViewProjection);
                float inverseW = MathF.Abs(homogeneous.w) > 0.000001f ? 1f / homogeneous.w : 1f;
                corners[index++] = new Vector3(
                    homogeneous.x * inverseW,
                    homogeneous.y * inverseW,
                    homogeneous.z * inverseW);
            }
        }

        return corners;
    }

    private static (float near, float far) ProjectionRange(Matrix projection)
    {
        if (MathF.Abs(projection.m43) > 0.5f)
        {
            float near = -projection.m34 / projection.m33;
            float far = -projection.m34 / (projection.m33 - 1f);
            return (near, far);
        }

        return (-projection.m34 / projection.m33, (1f - projection.m34) / projection.m33);
    }

    private static float CascadeDistance(float near, float far, float normalizedIndex)
    {
        const float logarithmicWeight = 0.65f;
        float logarithmic = near * MathF.Pow(far / near, normalizedIndex);
        float uniform = near + (far - near) * normalizedIndex;
        return uniform + (logarithmic - uniform) * logarithmicWeight;
    }

    private sealed record PassExecution(
        IRenderPipelineExecutor executor,
        RenderPipelineOperation operation);

    private readonly record struct ShadowBuildResult(
        RenderTextureHandle atlas,
        DirectionalShadowData? data);

    private readonly record struct ClusterBuildResult(
        RenderBufferHandle grid,
        RenderBufferHandle indices,
        IReadOnlyList<RenderUniformBinding>? values)
    {
        public bool isValid => grid.isValid && indices.isValid;

        public IReadOnlyList<RenderUniformBinding> uniforms => values
            ?? Array.Empty<RenderUniformBinding>();
    }
}
