using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class GraphicsRenderRuntime : IDisposable, IScenePassRuntimeBackend, IShadowPassRuntimeBackend
{
    private const int MaxShadowCascades = 2;
    private readonly IGraphicsDevice m_device;
    private readonly IGraphicsSwapchain m_swapchain;
    private readonly IGraphicsCommandList m_commandList;
    private readonly GpuResourceRegistry m_gpuResources;
    private readonly PipelineStateLibrary m_pipelineLibrary;
    private readonly Mesh m_builtinQuadMesh = BuiltinMeshFactory.CreateFullscreenQuad();
    private readonly Mesh m_builtinCubeMesh = BuiltinMeshFactory.CreateUnitCube();
    private readonly Dictionary<string, RenderTarget> m_graphTransientTargets = new(StringComparer.Ordinal);
    private readonly IGraphicsResourceSet m_fallbackShadowResourceSet;
    private readonly RuntimePassFeatureRegistry m_passFeatures = new();
    private readonly RenderableResolverRegistry m_renderableResolvers = new();
    private readonly MaterialTextureResolverRegistry m_textureResolvers = new();
    private IMaterialParameterBinder m_materialParameterBinder;
    private readonly GlobalParameterBinder m_globalParameterBinder = new();
    private readonly ScenePassExecutor m_scenePassExecutor = new();
    private readonly ShadowPassExecutor m_shadowPassExecutor = new();
    private IGraphicsRenderTarget? m_shadowRenderTarget;
    private IGraphicsResourceSet? m_shadowResourceSet;
    private int m_shadowMapSize;
    private readonly ShadowCascadeData[] m_shadowCascades =
    [
        new ShadowCascadeData(Matrix.identity, Matrix.identity, Matrix.identity, 0.0f, Vector4.ZERO),
        new ShadowCascadeData(Matrix.identity, Matrix.identity, Matrix.identity, 0.0f, Vector4.ZERO)
    ];
    private int m_shadowCascadeCount = 1;
    private bool m_shadowMapReady;
    private Viewport m_mainViewport;
    private ClearValue m_mainClearValue;
    private IGraphicsRenderTarget? m_mainRenderTarget;
    private bool m_frameActive;
    private bool m_passActive;
    private IGraphicsRenderTarget? m_activeRenderTarget;
    private readonly HashSet<IGraphicsRenderTarget> m_clearedTargets = [];

    public GraphicsRenderRuntime(IGraphicsDevice device, IGraphicsSwapchain swapchain, string? shaderProfile = null, string? shaderAssetRoot = null)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_swapchain = swapchain ?? throw new ArgumentNullException(nameof(swapchain));
        m_commandList = m_device.CreateCommandList();
        var resolvedShaderProfile = string.IsNullOrWhiteSpace(shaderProfile) ? GetDefaultShaderProfile() : shaderProfile;
        var resolvedShaderAssetRoot = string.IsNullOrWhiteSpace(shaderAssetRoot)
            ? Path.Combine(AppContext.BaseDirectory, "Assets")
            : shaderAssetRoot;
        m_gpuResources = new GpuResourceRegistry(m_device, m_swapchain);
        m_pipelineLibrary = new PipelineStateLibrary(m_device, resolvedShaderProfile, resolvedShaderAssetRoot);
        m_materialParameterBinder = new DefaultMaterialParameterBinder();
        m_pipelineLibrary.SetPipelineDescriptorFactory(new DefaultRenderPipelineDescriptorFactory());
        m_fallbackShadowResourceSet = m_device.CreateResourceSet(new ResourceSetDescription
        {
            bindings =
            [
                new GraphicsResourceBinding
                {
                    slot = 1,
                    bindingType = GraphicsBindingType.Texture,
                    resource = m_gpuResources.fallbackWhiteTexture
                }
            ]
        });
        RegisterDefaultShaderResolvers();
        RegisterDefaultRenderableResolvers();
        RegisterDefaultTextureResolvers();
        RegisterDefaultPassFeatures();
    }

    public void BeginFrame(RenderRequest request)
    {
        if (m_frameActive)
        {
            throw new InvalidOperationException("Render frame is already active.");
        }

        m_mainRenderTarget = GetOrCreateRenderTarget(request.target);
        var viewport = request.view.viewport;
        if (viewport.width <= 1 || viewport.height <= 1)
        {
            viewport = new Viewport(0, 0, m_mainRenderTarget.width, m_mainRenderTarget.height);
        }

        var clear = request.view.clear;
        m_mainViewport = viewport;
        m_mainClearValue = new ClearValue(clear.color.r, clear.color.g, clear.color.b, clear.color.a, clear.depth, clear.stencil);
        m_shadowMapReady = false;
        m_shadowCascadeCount = 1;
        m_activeRenderTarget = null;
        m_clearedTargets.Clear();

        m_device.BeginFrame();
        m_commandList.Begin();
        m_frameActive = true;
        m_passActive = false;
    }

    internal void BeginGraphPass(
        RenderPass pass,
        RenderGraphPassDeclaration declaration,
        RenderGraphFrameResources frameResources,
        RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(frameResources);
        ArgumentNullException.ThrowIfNull(request);

        if (!m_frameActive)
        {
            return;
        }

        if (pass is ShadowPass)
        {
            EndActivePassIfAny();
            return;
        }

        var target = ResolvePassRenderTarget(declaration, frameResources) ?? request.target;
        var gpuTarget = GetOrCreateRenderTarget(target);
        var viewport = ResolveViewportForTarget(target, request.view.viewport);
        var clearValue = ResolveClearValueForTarget(target, request.view.clear);
        EnsureRenderPass(gpuTarget, viewport, clearValue, clearOnFirstUse: true);
    }

    public void ExecutePass(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        if (!m_frameActive)
        {
            return;
        }

        if (m_passFeatures.TryExecute(context, renderList, filter))
        {
            return;
        }

        throw new InvalidOperationException($"No runtime pass feature registered for filter '{filter}'.");
    }

    internal void ExecuteShadowPassFeature(RenderPipelineContext context, RenderList renderList)
    {
        ExecuteShadowMapPass(context, renderList);
    }

    internal void ExecuteScenePassFeature(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        m_scenePassExecutor.Execute(this, context, renderList, filter);
    }

    internal void RegisterPassFeature(IRuntimePassFeature feature)
    {
        m_passFeatures.Register(feature);
    }

    internal void RegisterShaderResolver(IMaterialShaderResolver resolver)
    {
        m_pipelineLibrary.RegisterShaderResolver(resolver);
    }

    internal void RegisterRenderableResolver(IRenderableResolver resolver)
    {
        m_renderableResolvers.Register(resolver);
    }

    internal void RegisterTextureResolver(IMaterialTextureResolver resolver)
    {
        m_textureResolvers.Register(resolver);
    }

    internal void SetPipelineDescriptorFactory(IRenderPipelineDescriptorFactory factory)
    {
        m_pipelineLibrary.SetPipelineDescriptorFactory(factory ?? throw new ArgumentNullException(nameof(factory)));
    }

    internal void SetMaterialParameterBinder(IMaterialParameterBinder binder)
    {
        m_materialParameterBinder = binder ?? throw new ArgumentNullException(nameof(binder));
    }

    IGraphicsCommandList IScenePassRuntimeBackend.commandList => m_commandList;

    bool IScenePassRuntimeBackend.shadowMapReady => m_shadowMapReady;

    IGraphicsResourceSet? IScenePassRuntimeBackend.shadowResourceSet => m_shadowResourceSet;

    IGraphicsResourceSet IScenePassRuntimeBackend.fallbackShadowResourceSet => m_fallbackShadowResourceSet;

    void IScenePassRuntimeBackend.EnsureMainRenderPassStarted() => EnsureMainRenderPassStarted();

    void IScenePassRuntimeBackend.ApplyGlobalLightUniform(RenderScene scene) => ApplyGlobalLightUniform(scene);

    void IScenePassRuntimeBackend.ApplyCameraUniform(Camera camera) => ApplyCameraUniform(camera);

    void IScenePassRuntimeBackend.ApplyShadowUniforms(RenderScene scene) => ApplyShadowUniforms(scene);

    void IScenePassRuntimeBackend.ApplyShadowReceiverUniform(Renderable renderable, Material material) => ApplyShadowReceiverUniform(renderable, material);

    bool IScenePassRuntimeBackend.TryResolveDrawable(Renderable renderable, out Mesh mesh, out Material material, out Transform transform)
    {
        return TryResolveDrawable(renderable, out mesh, out material, out transform);
    }

    RuntimeGpuMesh IScenePassRuntimeBackend.GetOrCreateMesh(Mesh mesh) => GetOrCreateMesh(mesh);

    IGraphicsRenderPipeline IScenePassRuntimeBackend.GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout, RenderItemFilter filter)
    {
        return GetOrCreatePipeline(material, inputLayout, filter);
    }

    IGraphicsResourceSet IScenePassRuntimeBackend.GetOrCreateResourceSet(Renderable renderable, Material material)
    {
        return GetOrCreateResourceSet(renderable, material);
    }

    void IScenePassRuntimeBackend.BindMaterialParameters(Renderable renderable, Material material)
    {
        m_materialParameterBinder.Bind(m_commandList, renderable, material);
    }

    float IScenePassRuntimeBackend.GetAspectRatio(RenderView view, RenderTarget target) => GetAspectRatio(view, target);

    void IScenePassRuntimeBackend.WriteColumnMajor(Matrix matrix, Span<float> output) => WriteColumnMajor(matrix, output);

    IGraphicsCommandList IShadowPassRuntimeBackend.commandList => m_commandList;

    IGraphicsRenderTarget? IShadowPassRuntimeBackend.shadowRenderTarget => m_shadowRenderTarget;

    bool IShadowPassRuntimeBackend.hasShadowSamplingResource => m_shadowResourceSet is not null;

    int IShadowPassRuntimeBackend.shadowCascadeCount => m_shadowCascadeCount;

    ShadowCascadeData IShadowPassRuntimeBackend.GetShadowCascade(int cascadeIndex) => m_shadowCascades[cascadeIndex];

    void IShadowPassRuntimeBackend.MarkShadowMapReady() => m_shadowMapReady = true;

    LightShadowSettings IShadowPassRuntimeBackend.ResolveDirectionalShadowSettings(RenderScene scene) => ResolveDirectionalShadowSettings(scene);

    void IShadowPassRuntimeBackend.EnsureShadowResources(int requestedSize) => EnsureShadowResources(requestedSize);

    bool IShadowPassRuntimeBackend.TryBuildDirectionalShadowCascades(
        RenderRequest request,
        RenderScene scene,
        IReadOnlyList<RenderItem> casterItems,
        LightShadowSettings shadowSettings)
    {
        return TryBuildDirectionalShadowCascades(request, scene, casterItems, shadowSettings);
    }

    bool IShadowPassRuntimeBackend.TryResolveDrawable(Renderable renderable, out Mesh mesh, out Material material, out Transform transform)
    {
        return TryResolveDrawable(renderable, out mesh, out material, out transform);
    }

    RuntimeGpuMesh IShadowPassRuntimeBackend.GetOrCreateMesh(Mesh mesh) => GetOrCreateMesh(mesh);

    IGraphicsRenderPipeline IShadowPassRuntimeBackend.GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout, RenderItemFilter filter)
    {
        return GetOrCreatePipeline(material, inputLayout, filter);
    }

    void IShadowPassRuntimeBackend.WriteColumnMajor(Matrix matrix, Span<float> output) => WriteColumnMajor(matrix, output);

    void IShadowPassRuntimeBackend.SetMatrixRows(string uniformPrefix, Matrix matrix) => SetMatrixRows(uniformPrefix, matrix);

    private void RegisterDefaultPassFeatures()
    {
        RegisterPassFeature(new ShadowRuntimePassFeature(this));
        RegisterPassFeature(new SceneRuntimePassFeature(this, new[]
        {
            RenderItemFilter.Opaque,
            RenderItemFilter.Transparent,
            RenderItemFilter.DepthOnly,
            RenderItemFilter.Skybox,
            RenderItemFilter.Gizmo,
            RenderItemFilter.Ui,
            RenderItemFilter.PostProcess,
            RenderItemFilter.ObjectPicking
        }));
    }

    private void RegisterDefaultShaderResolvers()
    {
        m_pipelineLibrary.RegisterShaderResolver(new StandardMaterialShaderResolver());
        m_pipelineLibrary.RegisterShaderResolver(new CustomMaterialShaderResolver());
    }

    private void RegisterDefaultRenderableResolvers()
    {
        m_renderableResolvers.Register(new MeshRenderableResolver());
        m_renderableResolvers.Register(new SpriteRenderableResolver());
        m_renderableResolvers.Register(new FullscreenQuadRenderableResolver());
        m_renderableResolvers.Register(new SkyboxRenderableResolver());
    }

    private void RegisterDefaultTextureResolvers()
    {
        m_textureResolvers.Register(new DefaultMaterialTextureResolver());
    }

    private void ExecuteShadowMapPass(RenderPipelineContext context, RenderList renderList)
    {
        m_shadowPassExecutor.Execute(this, context, renderList);
    }

    private void EnsureMainRenderPassStarted()
    {
        if (m_mainRenderTarget is null)
        {
            return;
        }

        EnsureRenderPass(m_mainRenderTarget, m_mainViewport, m_mainClearValue, clearOnFirstUse: true);
    }

    private void EnsureRenderPass(
        IGraphicsRenderTarget renderTarget,
        Viewport viewport,
        ClearValue clearValue,
        bool clearOnFirstUse)
    {
        var sameTarget = m_passActive && ReferenceEquals(m_activeRenderTarget, renderTarget);
        if (!sameTarget)
        {
            EndActivePassIfAny();
            var shouldClear = !clearOnFirstUse || !m_clearedTargets.Contains(renderTarget);
            var useClear = shouldClear ? clearValue : new ClearValue(0, 0, 0, 0, 1.0f, 0);
            m_commandList.BeginRenderPass(renderTarget, useClear);
            if (clearOnFirstUse)
            {
                m_clearedTargets.Add(renderTarget);
            }

            m_activeRenderTarget = renderTarget;
            m_passActive = true;
        }

        m_commandList.SetViewport(new GraphicsViewport(viewport.x, viewport.y, viewport.width, viewport.height));
    }

    private void EndActivePassIfAny()
    {
        if (!m_passActive)
        {
            return;
        }

        m_commandList.EndRenderPass();
        m_passActive = false;
        m_activeRenderTarget = null;
    }

    private RenderTarget? ResolvePassRenderTarget(RenderGraphPassDeclaration declaration, RenderGraphFrameResources frameResources)
    {
        for (var i = declaration.resources.Count - 1; i >= 0; i--)
        {
            var usage = declaration.resources[i];
            if (usage.access is not RenderGraphResourceAccess.Write and not RenderGraphResourceAccess.ReadWrite)
            {
                continue;
            }

            if (frameResources.TryResolveRenderTarget(usage.name, out var target))
            {
                return target;
            }

            if (frameResources.TryGetInternalDescriptor(usage.name, out var descriptor) && descriptor is not null)
            {
                return GetOrCreateGraphTransientTarget(usage.name, descriptor);
            }
        }

        return null;
    }

    private RenderTarget GetOrCreateGraphTransientTarget(string resourceName, RenderTargetDescriptor descriptor)
    {
        if (m_graphTransientTargets.TryGetValue(resourceName, out var existing))
        {
            if (existing is TextureRenderTarget textureTarget && IsSameDescriptor(textureTarget.descriptor, descriptor))
            {
                return existing;
            }

            m_graphTransientTargets.Remove(resourceName);
        }

        var created = new TextureRenderTarget(descriptor);
        m_graphTransientTargets.Add(resourceName, created);
        return created;
    }

    private static bool IsSameDescriptor(RenderTargetDescriptor lhs, RenderTargetDescriptor rhs)
    {
        return lhs.size.width == rhs.size.width
               && lhs.size.height == rhs.size.height
               && lhs.colorFormat == rhs.colorFormat
               && lhs.hasDepth == rhs.hasDepth
               && lhs.hasMipmaps == rhs.hasMipmaps;
    }

    private static Viewport ResolveViewportForTarget(RenderTarget target, Viewport requested)
    {
        if (requested.width > 1 && requested.height > 1
            && requested.width <= target.width && requested.height <= target.height)
        {
            return requested;
        }

        return new Viewport(0, 0, target.width, target.height);
    }

    private static ClearValue ResolveClearValueForTarget(RenderTarget target, ClearSettings clearSettings)
    {
        var clear = clearSettings;
        if (target is TextureRenderTarget)
        {
            return new ClearValue(0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0);
        }

        return new ClearValue(clear.color.r, clear.color.g, clear.color.b, clear.color.a, clear.depth, clear.stencil);
    }

    public void EndFrame()
    {
        if (!m_frameActive)
        {
            return;
        }

        EndActivePassIfAny();

        m_commandList.End();
        m_device.Submit(m_commandList);
        m_device.EndFrame();
        m_frameActive = false;
    }

    public void Dispose()
    {
        m_pipelineLibrary.Dispose();
        m_gpuResources.Dispose();
        m_graphTransientTargets.Clear();
        m_shadowResourceSet?.Dispose();
        m_shadowResourceSet = null;
        m_fallbackShadowResourceSet.Dispose();
        m_shadowRenderTarget?.Dispose();
        m_shadowRenderTarget = null;
        m_commandList.Dispose();
    }

    private IGraphicsRenderTarget GetOrCreateRenderTarget(RenderTarget target)
    {
        return m_gpuResources.GetOrCreateRenderTarget(target);
    }

    private RuntimeGpuMesh GetOrCreateMesh(Mesh mesh)
    {
        return m_gpuResources.GetOrCreateMesh(mesh);
    }

    private IGraphicsRenderPipeline GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout, RenderItemFilter filter)
    {
        return m_pipelineLibrary.GetOrCreatePipeline(material, inputLayout, filter);
    }

    private IGraphicsResourceSet GetOrCreateResourceSet(Renderable renderable, Material material)
    {
        var texture = m_textureResolvers.Resolve(renderable, material);
        var gpuTexture = texture is null ? m_gpuResources.fallbackWhiteTexture : m_gpuResources.GetOrCreateTexture(texture);
        return m_gpuResources.GetOrCreateTextureResourceSet(gpuTexture, 0);
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

    private void EnsureShadowResources(int requestedSize)
    {
        var size = Math.Max(1, requestedSize);
        if (m_shadowRenderTarget is not null && m_shadowResourceSet is not null && m_shadowMapSize == size)
        {
            return;
        }

        m_shadowResourceSet?.Dispose();
        m_shadowResourceSet = null;
        m_shadowRenderTarget?.Dispose();
        m_shadowRenderTarget = null;
        m_shadowMapSize = size;

        m_shadowRenderTarget = m_device.CreateRenderTarget(new GraphicsRenderTargetDescription
        {
            width = size,
            height = size,
            colorFormats = [PixelFormat.R16G16B16A16Float],
            depthFormat = PixelFormat.D24UnormS8Uint
        });

        if (m_shadowRenderTarget.colorAttachments.Count == 0)
        {
            return;
        }

        m_shadowResourceSet = m_device.CreateResourceSet(new ResourceSetDescription
        {
            bindings =
            [
                new GraphicsResourceBinding
                {
                    slot = 1,
                    bindingType = GraphicsBindingType.Texture,
                    resource = m_shadowRenderTarget.colorAttachments[0]
                }
            ]
        });
    }

    private bool TryBuildDirectionalShadowCascades(
        RenderRequest request,
        RenderScene scene,
        IReadOnlyList<RenderItem> casterItems,
        LightShadowSettings shadowSettings)
    {
        var lightDirection = ResolveDirectionalLightDirection(scene);
        if (lightDirection.LengthSquared() <= 0.00001f)
        {
            return false;
        }

        var cascadeCount = Math.Clamp(shadowSettings.cascadeCount, 1, MaxShadowCascades);
        m_shadowCascadeCount = cascadeCount;

        if (cascadeCount == 1)
        {
            return TryBuildSingleCascadeShadow(scene, casterItems);
        }

        var view = request.view;
        var target = request.target;
        var camera = view.camera;
        var aspect = GetAspectRatio(view, target);
        var cameraNear = MathF.Max(0.01f, camera.nearClip);
        var cameraFar = MathF.Max(cameraNear + 0.01f, camera.farClip);

        var cameraView = camera.GetViewMatrix();
        var cameraProjection = camera.GetProjectionMatrix(aspect);
        var inverseViewProjection = Matrix.Invert(cameraProjection * cameraView);

        Span<Vector3> nearPlaneCorners = stackalloc Vector3[4];
        Span<Vector3> farPlaneCorners = stackalloc Vector3[4];
        BuildCameraFrustumNearFarCorners(inverseViewProjection, nearPlaneCorners, farPlaneCorners);

        Span<float> cascadeSplits = stackalloc float[MaxShadowCascades];
        var lambda = Math.Clamp(shadowSettings.cascadeSplitLambda, 0.0f, 1.0f);
        for (var i = 0; i < cascadeCount; i++)
        {
            var t = (i + 1.0f) / cascadeCount;
            var log = cameraNear * MathF.Pow(cameraFar / cameraNear, t);
            var uniform = cameraNear + (cameraFar - cameraNear) * t;
            cascadeSplits[i] = uniform + (log - uniform) * lambda;
        }

        var foundCasters = false;
        foreach (var item in casterItems)
        {
            if (item.renderable is MeshRenderable)
            {
                foundCasters = true;
                break;
            }
        }
        if (!foundCasters)
        {
            return false;
        }

        var tileScaleX = 1.0f / cascadeCount;
        var tileWidth = Math.Max(1, m_shadowMapSize / cascadeCount);

        Span<Vector3> corners = stackalloc Vector3[8];
        for (var cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
        {
            var splitNear = cascadeIndex == 0 ? cameraNear : cascadeSplits[cascadeIndex - 1];
            var splitFar = cascadeSplits[cascadeIndex];
            var nearRatio = (splitNear - cameraNear) / (cameraFar - cameraNear);
            var farRatio = (splitFar - cameraNear) / (cameraFar - cameraNear);

            for (var i = 0; i < 4; i++)
            {
                var ray = farPlaneCorners[i] - nearPlaneCorners[i];
                corners[i] = nearPlaneCorners[i] + ray * nearRatio;
                corners[i + 4] = nearPlaneCorners[i] + ray * farRatio;
            }

            var center = Vector3.ZERO;
            for (var i = 0; i < 8; i++)
            {
                center += corners[i];
            }

            center /= 8.0f;
            var radius = 0.0f;
            for (var i = 0; i < 8; i++)
            {
                radius = MathF.Max(radius, Vector3.Distance(corners[i], center));
            }

            radius = MathF.Max(radius, 6.0f);
            var lightPos = center - lightDirection * (radius * 2.0f + 30.0f);
            var up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.UP)) > 0.98f ? Vector3.RIGHT : Vector3.UP;
            var lightView = Matrix.CreateLookAtRH(lightPos, center, up);

            var lightMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var lightMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (var i = 0; i < corners.Length; i++)
            {
                var p = Vector3.Transform(corners[i], lightView);
                lightMin.x = MathF.Min(lightMin.x, p.x);
                lightMin.y = MathF.Min(lightMin.y, p.y);
                lightMin.z = MathF.Min(lightMin.z, p.z);
                lightMax.x = MathF.Max(lightMax.x, p.x);
                lightMax.y = MathF.Max(lightMax.y, p.y);
                lightMax.z = MathF.Max(lightMax.z, p.z);
            }

            var extentX = lightMax.x - lightMin.x;
            var extentY = lightMax.y - lightMin.y;
            var texelWorldSize = MathF.Max(extentX, extentY) / tileWidth;
            if (texelWorldSize > 0.0f)
            {
                var centerLs = Vector3.Transform(center, lightView);
                centerLs.x = MathF.Floor(centerLs.x / texelWorldSize) * texelWorldSize;
                centerLs.y = MathF.Floor(centerLs.y / texelWorldSize) * texelWorldSize;
                var halfX = extentX * 0.5f;
                var halfY = extentY * 0.5f;
                lightMin.x = centerLs.x - halfX;
                lightMax.x = centerLs.x + halfX;
                lightMin.y = centerLs.y - halfY;
                lightMax.y = centerLs.y + halfY;
            }

            // In RH light view space, visible geometry is typically on negative Z.
            // Convert to positive view distances before building RH orthographic projection.
            const float zMargin = 50.0f;
            var nearDist = -lightMax.z;
            var farDist = -lightMin.z;
            var near = MathF.Max(0.1f, nearDist - zMargin);
            var far = MathF.Max(near + 1.0f, farDist + zMargin);
            var lightProjection = CreateOrthographicOffCenterRH(lightMin.x, lightMax.x, lightMin.y, lightMax.y, near, far);
            var viewProjection = lightProjection * lightView;
            var atlasScaleBias = new Vector4(tileScaleX, 1.0f, cascadeIndex * tileScaleX, 0.0f);

            m_shadowCascades[cascadeIndex] = new ShadowCascadeData(lightView, lightProjection, viewProjection, splitFar, atlasScaleBias);
        }

        for (var i = cascadeCount; i < MaxShadowCascades; i++)
        {
            m_shadowCascades[i] = new ShadowCascadeData(Matrix.identity, Matrix.identity, Matrix.identity, cameraFar, Vector4.ZERO);
        }

        return true;
    }

    private bool TryBuildSingleCascadeShadow(RenderScene scene, IReadOnlyList<RenderItem> casterItems)
    {
        var lightDirection = ResolveDirectionalLightDirection(scene);
        if (lightDirection.LengthSquared() <= 0.00001f)
        {
            return false;
        }

        var foundCasters = false;
        foreach (var item in casterItems)
        {
            if (item.renderable is MeshRenderable)
            {
                foundCasters = true;
                break;
            }
        }

        if (!foundCasters)
        {
            return false;
        }

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        var foundAnyMesh = false;

        // Include all visible mesh bounds (casters + receivers) so the projected shadow can land on receiver surfaces.
        foreach (var renderable in scene.renderables.items)
        {
            if (renderable is not MeshRenderable meshRenderable || renderable.visibility != Visibility.Visible)
            {
                continue;
            }

            var worldBounds = ComputeWorldBounds(meshRenderable.mesh.bounds, meshRenderable.transform.ToMatrix());
            var worldMin = worldBounds.center - worldBounds.extents;
            var worldMax = worldBounds.center + worldBounds.extents;

            min.x = MathF.Min(min.x, worldMin.x);
            min.y = MathF.Min(min.y, worldMin.y);
            min.z = MathF.Min(min.z, worldMin.z);
            max.x = MathF.Max(max.x, worldMax.x);
            max.y = MathF.Max(max.y, worldMax.y);
            max.z = MathF.Max(max.z, worldMax.z);
            foundAnyMesh = true;
        }

        if (!foundAnyMesh)
        {
            return false;
        }

        var center = (min + max) * 0.5f;
        var extents = (max - min) * 0.5f;
        var radius = MathF.Max(extents.x, MathF.Max(extents.y, extents.z));
        radius = MathF.Max(radius, 6.0f);

        var lightPos = center - lightDirection * (radius * 3.0f + 20.0f);
        var up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.UP)) > 0.98f ? Vector3.RIGHT : Vector3.UP;
        var lightView = Matrix.CreateLookAtRH(lightPos, center, up);

        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(min.x, min.y, max.z);
        corners[2] = new Vector3(min.x, max.y, min.z);
        corners[3] = new Vector3(min.x, max.y, max.z);
        corners[4] = new Vector3(max.x, min.y, min.z);
        corners[5] = new Vector3(max.x, min.y, max.z);
        corners[6] = new Vector3(max.x, max.y, min.z);
        corners[7] = new Vector3(max.x, max.y, max.z);

        var lightMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var lightMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (var i = 0; i < corners.Length; i++)
        {
            var p = Vector3.Transform(corners[i], lightView);
            lightMin.x = MathF.Min(lightMin.x, p.x);
            lightMin.y = MathF.Min(lightMin.y, p.y);
            lightMin.z = MathF.Min(lightMin.z, p.z);
            lightMax.x = MathF.Max(lightMax.x, p.x);
            lightMax.y = MathF.Max(lightMax.y, p.y);
            lightMax.z = MathF.Max(lightMax.z, p.z);
        }

        const float margin = 10.0f;
        var left = lightMin.x - margin;
        var right = lightMax.x + margin;
        var bottom = lightMin.y - margin;
        var top = lightMax.y + margin;
        var nearDist = -lightMax.z;
        var farDist = -lightMin.z;
        var near = MathF.Max(0.1f, nearDist - margin);
        var far = MathF.Max(near + 1.0f, farDist + margin);
        var lightProjection = CreateOrthographicOffCenterRH(left, right, bottom, top, near, far);
        var viewProjection = lightProjection * lightView;

        m_shadowCascades[0] = new ShadowCascadeData(lightView, lightProjection, viewProjection, far, new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        m_shadowCascades[1] = new ShadowCascadeData(Matrix.identity, Matrix.identity, Matrix.identity, far, Vector4.ZERO);
        return true;
    }

    private static Matrix CreateOrthographicOffCenterRH(float left, float right, float bottom, float top, float near, float far)
    {
        var m00 = 2.0f / (right - left);
        var m11 = 2.0f / (top - bottom);
        var m22 = 1.0f / (near - far);
        var m14 = -(right + left) / (right - left);
        var m24 = -(top + bottom) / (top - bottom);
        var m34 = near / (near - far);

        return new Matrix(
            m00, 0.0f, 0.0f, m14,
            0.0f, m11, 0.0f, m24,
            0.0f, 0.0f, m22, m34,
            0.0f, 0.0f, 0.0f, 1.0f);
    }

    private static void BuildCameraFrustumNearFarCorners(Matrix inverseViewProjection, Span<Vector3> nearPlaneCorners, Span<Vector3> farPlaneCorners)
    {
        Span<Vector4> clipNear = stackalloc Vector4[4];
        clipNear[0] = new Vector4(-1.0f, -1.0f, 0.0f, 1.0f);
        clipNear[1] = new Vector4(1.0f, -1.0f, 0.0f, 1.0f);
        clipNear[2] = new Vector4(1.0f, 1.0f, 0.0f, 1.0f);
        clipNear[3] = new Vector4(-1.0f, 1.0f, 0.0f, 1.0f);

        Span<Vector4> clipFar = stackalloc Vector4[4];
        clipFar[0] = new Vector4(-1.0f, -1.0f, 1.0f, 1.0f);
        clipFar[1] = new Vector4(1.0f, -1.0f, 1.0f, 1.0f);
        clipFar[2] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        clipFar[3] = new Vector4(-1.0f, 1.0f, 1.0f, 1.0f);

        for (var i = 0; i < 4; i++)
        {
            var near = Vector4.Transform(clipNear[i], inverseViewProjection).ProjectToVector3();
            var far = Vector4.Transform(clipFar[i], inverseViewProjection).ProjectToVector3();
            nearPlaneCorners[i] = near;
            farPlaneCorners[i] = far;
        }
    }

    private static MeshBounds ComputeWorldBounds(MeshBounds localBounds, Matrix modelMatrix)
    {
        var center = localBounds.center;
        var extents = localBounds.extents;
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(center.x - extents.x, center.y - extents.y, center.z - extents.z);
        corners[1] = new Vector3(center.x - extents.x, center.y - extents.y, center.z + extents.z);
        corners[2] = new Vector3(center.x - extents.x, center.y + extents.y, center.z - extents.z);
        corners[3] = new Vector3(center.x - extents.x, center.y + extents.y, center.z + extents.z);
        corners[4] = new Vector3(center.x + extents.x, center.y - extents.y, center.z - extents.z);
        corners[5] = new Vector3(center.x + extents.x, center.y - extents.y, center.z + extents.z);
        corners[6] = new Vector3(center.x + extents.x, center.y + extents.y, center.z - extents.z);
        corners[7] = new Vector3(center.x + extents.x, center.y + extents.y, center.z + extents.z);

        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (var i = 0; i < corners.Length; i++)
        {
            var p = Vector3.Transform(corners[i], modelMatrix);
            min.x = MathF.Min(min.x, p.x);
            min.y = MathF.Min(min.y, p.y);
            min.z = MathF.Min(min.z, p.z);
            max.x = MathF.Max(max.x, p.x);
            max.y = MathF.Max(max.y, p.y);
            max.z = MathF.Max(max.z, p.z);
        }

        var worldCenter = (min + max) * 0.5f;
        var worldExtents = (max - min) * 0.5f;
        return new MeshBounds(worldCenter, worldExtents);
    }

    private void ApplyShadowUniforms(RenderScene scene)
    {
        m_globalParameterBinder.ApplyShadowUniforms(
            m_commandList,
            scene,
            m_shadowMapReady,
            m_shadowCascadeCount,
            m_shadowCascades,
            m_shadowMapSize);
    }

    private void ApplyShadowReceiverUniform(Renderable renderable, Material material)
    {
        m_globalParameterBinder.ApplyShadowReceiverUniform(m_commandList, renderable, material);
    }

    private void SetMatrixRows(string uniformPrefix, Matrix matrix)
    {
        m_globalParameterBinder.SetMatrixRows(m_commandList, uniformPrefix, matrix);
    }

    private void ApplyGlobalLightUniform(RenderScene scene)
    {
        m_globalParameterBinder.ApplyGlobalLightUniform(m_commandList, scene);
    }

    private void ApplyCameraUniform(Camera camera)
    {
        m_globalParameterBinder.ApplyCameraUniform(m_commandList, camera);
    }

    private bool TryResolveDrawable(Renderable renderable, out Mesh mesh, out Material material, out Transform transform)
    {
        return m_renderableResolvers.TryResolve(renderable, m_builtinQuadMesh, m_builtinCubeMesh, out mesh, out material, out transform);
    }

    private static float GetAspectRatio(RenderView view, RenderTarget target)
    {
        var width = view.viewport.width > 1 ? view.viewport.width : target.width;
        var height = view.viewport.height > 1 ? view.viewport.height : target.height;
        return height > 0 ? width / (float)height : 1.0f;
    }

    private static void WriteColumnMajor(Matrix matrix, Span<float> output)
    {
        if (output.Length < 16)
        {
            throw new ArgumentException("Matrix output span must contain at least 16 floats.", nameof(output));
        }

        output[0] = matrix.m11;
        output[1] = matrix.m21;
        output[2] = matrix.m31;
        output[3] = matrix.m41;
        output[4] = matrix.m12;
        output[5] = matrix.m22;
        output[6] = matrix.m32;
        output[7] = matrix.m42;
        output[8] = matrix.m13;
        output[9] = matrix.m23;
        output[10] = matrix.m33;
        output[11] = matrix.m43;
        output[12] = matrix.m14;
        output[13] = matrix.m24;
        output[14] = matrix.m34;
        output[15] = matrix.m44;
    }

    private static string GetDefaultShaderProfile()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "metal";
        }

        if (OperatingSystem.IsWindows())
        {
            return "dxbc";
        }

        if (OperatingSystem.IsLinux())
        {
            return "spirv";
        }

        return "glsl";
    }

}
