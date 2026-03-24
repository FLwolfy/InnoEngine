using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class GraphicsRenderRuntime : IDisposable
{
    private const int MaxShadowCascades = 2;
    private readonly IGraphicsDevice m_device;
    private readonly IGraphicsSwapchain m_swapchain;
    private readonly IGraphicsCommandList m_commandList;
    private readonly string m_shaderProfile;
    private readonly string m_shaderAssetRoot;
    private readonly Dictionary<Mesh, GpuMesh> m_meshCache = new();
    private readonly Dictionary<string, IGraphicsProgram> m_programCache = new(StringComparer.Ordinal);
    private readonly Dictionary<PipelineKey, IGraphicsRenderPipeline> m_pipelineCache = new();
    private readonly List<IGraphicsShader> m_shaderCache = [];
    private readonly Dictionary<Texture, IGraphicsTexture> m_textureCache = new();
    private readonly Dictionary<IGraphicsTexture, IGraphicsResourceSet> m_resourceSetCache = new();
    private readonly Mesh m_builtinQuadMesh = CreateFullscreenQuadMesh();
    private readonly Mesh m_builtinCubeMesh = CreateUnitCubeMesh();
    private readonly Dictionary<RenderTarget, IGraphicsRenderTarget> m_renderTargetCache = new();
    private readonly IGraphicsTexture m_fallbackWhiteTexture;
    private readonly IGraphicsResourceSet m_fallbackShadowResourceSet;
    private IGraphicsRenderTarget? m_backbufferTarget;
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

    private readonly record struct ShadowCascadeData(
        Matrix view,
        Matrix projection,
        Matrix viewProjection,
        float splitDistance,
        Vector4 atlasScaleBias);

    public GraphicsRenderRuntime(IGraphicsDevice device, IGraphicsSwapchain swapchain, string? shaderProfile = null, string? shaderAssetRoot = null)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_swapchain = swapchain ?? throw new ArgumentNullException(nameof(swapchain));
        m_commandList = m_device.CreateCommandList();
        m_shaderProfile = string.IsNullOrWhiteSpace(shaderProfile) ? GetDefaultShaderProfile() : shaderProfile;
        m_shaderAssetRoot = string.IsNullOrWhiteSpace(shaderAssetRoot)
            ? Path.Combine(AppContext.BaseDirectory, "Assets")
            : shaderAssetRoot;
        m_fallbackWhiteTexture = CreateFallbackWhiteTexture();
        m_fallbackShadowResourceSet = m_device.CreateResourceSet(new ResourceSetDescription
        {
            bindings =
            [
                new GraphicsResourceBinding
                {
                    slot = 1,
                    bindingType = GraphicsBindingType.Texture,
                    resource = m_fallbackWhiteTexture
                }
            ]
        });
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

        m_device.BeginFrame();
        m_commandList.Begin();
        m_frameActive = true;
        m_passActive = false;
    }

    public void ExecutePass(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        if (!m_frameActive)
        {
            return;
        }

        if (filter == RenderItemFilter.ShadowCasters)
        {
            ExecuteShadowMapPass(context, renderList);
            return;
        }

        EnsureMainRenderPassStarted();

        var view = context.request.view;
        var target = context.request.target;
        var overlayPass = filter is RenderItemFilter.Ui or RenderItemFilter.Gizmo or RenderItemFilter.PostProcess;
        var viewMatrix = overlayPass ? Matrix.identity : view.camera.GetViewMatrix();
        var projectionMatrix = overlayPass
            ? Matrix.identity
            : view.camera.GetProjectionMatrix(GetAspectRatio(view, target));

        Span<float> viewRaw = stackalloc float[16];
        Span<float> projRaw = stackalloc float[16];
        Span<float> modelRaw = stackalloc float[16];
        WriteColumnMajor(viewMatrix, viewRaw);
        WriteColumnMajor(projectionMatrix, projRaw);
        m_commandList.SetViewProjection(viewRaw, projRaw);
        ApplyGlobalLightUniform(context.request.scene);
        ApplyShadowUniforms(context.request.scene);
        Span<float> ambientRaw = stackalloc float[4];
        var ambient = context.request.scene.environment.ambientColor;
        ambientRaw[0] = ambient.r;
        ambientRaw[1] = ambient.g;
        ambientRaw[2] = ambient.b;
        ambientRaw[3] = context.request.scene.environment.ambientIntensity;
        m_commandList.SetGlobalVector4("u_ambient", ambientRaw);

        foreach (var item in renderList.items)
        {
            if (!TryResolveDrawable(item.renderable, out var mesh, out var material, out var transform))
            {
                continue;
            }

            var gpuMesh = GetOrCreateMesh(mesh);
            var pipeline = GetOrCreatePipeline(material, gpuMesh.inputLayout, filter);

            m_commandList.SetPipeline(pipeline);
            m_commandList.SetVertexBuffer(gpuMesh.vertexBuffer);
            var baseResourceSet = GetOrCreateResourceSet(item.renderable, material);
            m_commandList.SetResourceSet(0, baseResourceSet);
            if (m_shadowMapReady && material.receiveShadows && m_shadowResourceSet is not null)
            {
                m_commandList.SetResourceSet(1, m_shadowResourceSet);
            }
            else
            {
                m_commandList.SetResourceSet(1, m_fallbackShadowResourceSet);
            }
            ApplyShadowReceiverUniform(item.renderable, material);

            if (filter == RenderItemFilter.Skybox)
            {
                transform = new Transform
                {
                    position = view.camera.transform.position,
                    rotation = Quaternion.identity,
                    scale = new Vector3(50.0f, 50.0f, 50.0f)
                };
            }

            var modelMatrix = transform.ToMatrix();

            WriteColumnMajor(modelMatrix, modelRaw);
            m_commandList.SetModelTransform(modelRaw);

            if (gpuMesh.indexCount > 0)
            {
                m_commandList.SetIndexBuffer(gpuMesh.indexBuffer!);
                m_commandList.DrawIndexed(new DrawIndexedArguments(gpuMesh.indexCount));
                context.frame.statistics.drawCalls++;
            }
            else if (gpuMesh.vertexCount > 0)
            {
                m_commandList.Draw(gpuMesh.vertexCount);
                context.frame.statistics.drawCalls++;
            }
        }
    }

    private void ExecuteShadowMapPass(RenderPipelineContext context, RenderList renderList)
    {
        if (!context.request.scene.settings.enableShadows)
        {
            return;
        }

        var shadowSettings = ResolveDirectionalShadowSettings(context.request.scene);
        if (!shadowSettings.enabled)
        {
            return;
        }

        EnsureShadowResources(shadowSettings.resolution);
        if (m_shadowRenderTarget is null || m_shadowResourceSet is null)
        {
            return;
        }

        if (!TryBuildDirectionalShadowCascades(context.request, context.request.scene, renderList.items, shadowSettings))
        {
            return;
        }

        var clearShadow = new ClearValue(1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0);
        m_commandList.BeginRenderPass(m_shadowRenderTarget, clearShadow);
        Span<float> modelRaw = stackalloc float[16];
        Span<float> viewRaw = stackalloc float[16];
        Span<float> projRaw = stackalloc float[16];
        var tileWidth = Math.Max(1, m_shadowRenderTarget.width / m_shadowCascadeCount);
        for (var cascadeIndex = 0; cascadeIndex < m_shadowCascadeCount; cascadeIndex++)
        {
            var cascade = m_shadowCascades[cascadeIndex];
            m_commandList.SetViewport(new GraphicsViewport(tileWidth * cascadeIndex, 0, tileWidth, m_shadowRenderTarget.height));
            SetMatrixRows("u_shadowViewProj", cascade.viewProjection);

            WriteColumnMajor(cascade.view, viewRaw);
            WriteColumnMajor(cascade.projection, projRaw);
            m_commandList.SetViewProjection(viewRaw, projRaw);

            foreach (var item in renderList.items)
            {
                if (!TryResolveDrawable(item.renderable, out var mesh, out var material, out var transform))
                {
                    continue;
                }

                if (item.renderable is not MeshRenderable)
                {
                    continue;
                }

                var gpuMesh = GetOrCreateMesh(mesh);
                var pipeline = GetOrCreatePipeline(material, gpuMesh.inputLayout, RenderItemFilter.ShadowCasters);
                m_commandList.SetPipeline(pipeline);
                m_commandList.SetVertexBuffer(gpuMesh.vertexBuffer);

                var modelMatrix = transform.ToMatrix();
                WriteColumnMajor(modelMatrix, modelRaw);
                m_commandList.SetModelTransform(modelRaw);

                if (gpuMesh.indexCount > 0)
                {
                    m_commandList.SetIndexBuffer(gpuMesh.indexBuffer!);
                    m_commandList.DrawIndexed(new DrawIndexedArguments(gpuMesh.indexCount));
                    context.frame.statistics.drawCalls++;
                }
                else if (gpuMesh.vertexCount > 0)
                {
                    m_commandList.Draw(gpuMesh.vertexCount);
                    context.frame.statistics.drawCalls++;
                }
            }
        }

        m_commandList.EndRenderPass();
        m_shadowMapReady = true;
    }

    private void EnsureMainRenderPassStarted()
    {
        if (m_passActive)
        {
            return;
        }

        if (m_mainRenderTarget is null)
        {
            return;
        }

        m_commandList.BeginRenderPass(m_mainRenderTarget, m_mainClearValue);
        m_commandList.SetViewport(new GraphicsViewport(m_mainViewport.x, m_mainViewport.y, m_mainViewport.width, m_mainViewport.height));
        m_passActive = true;
    }

    public void EndFrame()
    {
        if (!m_frameActive)
        {
            return;
        }

        if (m_passActive)
        {
            m_commandList.EndRenderPass();
            m_passActive = false;
        }

        m_commandList.End();
        m_device.Submit(m_commandList);
        m_device.EndFrame();
        m_frameActive = false;
    }

    public void Dispose()
    {
        foreach (var pipeline in m_pipelineCache.Values)
        {
            pipeline.Dispose();
        }

        foreach (var program in m_programCache.Values)
        {
            program.Dispose();
        }

        foreach (var shader in m_shaderCache)
        {
            shader.Dispose();
        }

        foreach (var mesh in m_meshCache.Values)
        {
            mesh.Dispose();
        }

        foreach (var renderTarget in m_renderTargetCache.Values)
        {
            renderTarget.Dispose();
        }

        foreach (var resourceSet in m_resourceSetCache.Values)
        {
            resourceSet.Dispose();
        }

        foreach (var texture in m_textureCache.Values)
        {
            texture.Dispose();
        }

        m_renderTargetCache.Clear();
        m_resourceSetCache.Clear();
        m_textureCache.Clear();
        m_shadowResourceSet?.Dispose();
        m_shadowResourceSet = null;
        m_fallbackShadowResourceSet.Dispose();
        m_shadowRenderTarget?.Dispose();
        m_shadowRenderTarget = null;
        m_backbufferTarget?.Dispose();
        m_fallbackWhiteTexture.Dispose();
        m_commandList.Dispose();
    }

    private IGraphicsRenderTarget GetOrCreateRenderTarget(RenderTarget target)
    {
        if (target is BackbufferTarget backbuffer)
        {
            if (backbuffer.window.width != m_swapchain.width || backbuffer.window.height != m_swapchain.height)
            {
                m_swapchain.Resize(backbuffer.window.width, backbuffer.window.height);
            }

            if (m_backbufferTarget is null || m_backbufferTarget.width != backbuffer.window.width || m_backbufferTarget.height != backbuffer.window.height)
            {
                m_backbufferTarget?.Dispose();
                m_backbufferTarget = m_device.CreateRenderTarget(new GraphicsRenderTargetDescription
                {
                    width = backbuffer.window.width,
                    height = backbuffer.window.height,
                    colorFormats = [m_swapchain.colorFormat],
                    depthFormat = m_swapchain.depthFormat,
                    useBackbuffer = true
                });
            }

            return m_backbufferTarget;
        }

        if (m_renderTargetCache.TryGetValue(target, out var cached))
        {
            return cached;
        }

        var created = m_device.CreateRenderTarget(new GraphicsRenderTargetDescription
        {
            width = Math.Max(1, target.width),
            height = Math.Max(1, target.height),
            colorFormats = [m_swapchain.colorFormat],
            depthFormat = m_swapchain.depthFormat
        });
        m_renderTargetCache.Add(target, created);
        return created;
    }

    private GpuMesh GetOrCreateMesh(Mesh mesh)
    {
        if (m_meshCache.TryGetValue(mesh, out var cached))
        {
            return cached;
        }

        var vertexBytes = mesh.vertexData;
        var vertexBufferSize = Math.Max(1, vertexBytes.Length);
        var vertexBuffer = m_device.CreateBuffer(new BufferDescription
        {
            sizeInBytes = vertexBufferSize,
            usage = GraphicsBufferUsage.Vertex,
            cpuAccess = BufferCpuAccess.Write
        });
        if (vertexBytes.Length > 0)
        {
            vertexBuffer.SetData(vertexBytes.Span);
        }

        IGraphicsBuffer? indexBuffer = null;
        var indexCount = mesh.indices.Length;
        if (indexCount > 0)
        {
            indexBuffer = m_device.CreateBuffer(new BufferDescription
            {
                sizeInBytes = indexCount * sizeof(uint),
                usage = GraphicsBufferUsage.Index,
                cpuAccess = BufferCpuAccess.Write
            });
            indexBuffer.SetData(mesh.indices.Span);
        }

        var inputLayout = CreateInputLayout(mesh.vertexLayout);
        var created = new GpuMesh(mesh, vertexBuffer, indexBuffer, mesh.vertexCount, indexCount, inputLayout);
        m_meshCache.Add(mesh, created);
        return created;
    }

    private IGraphicsRenderPipeline GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout, RenderItemFilter filter)
    {
        var shaderName = GetShaderName(material);
        if (filter == RenderItemFilter.ShadowCasters)
        {
            shaderName = "shadowmap";
        }

        var key = new PipelineKey(shaderName, material.surfaceType, material.blendMode, material.cullMode, material.depthMode, inputLayout, filter);
        if (m_pipelineCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var program = GetOrCreateProgram(shaderName);
        var isDepthOnlyPass = filter == RenderItemFilter.DepthOnly;
        var isShadowPass = filter == RenderItemFilter.ShadowCasters;
        var isOverlayPass = filter is RenderItemFilter.Ui or RenderItemFilter.Gizmo or RenderItemFilter.PostProcess;
        var isSkyboxPass = filter == RenderItemFilter.Skybox;
        var pipeline = m_device.CreateRenderPipeline(new GraphicsRenderPipelineDescription
        {
            program = program,
            inputLayout = inputLayout,
            rasterState = new GraphicsRasterState
            {
                cullMode = isSkyboxPass
                    ? GraphicsCullMode.None
                    : isShadowPass
                        ? GraphicsCullMode.Back
                    : material.cullMode switch
                {
                    MaterialCullMode.Front => GraphicsCullMode.Front,
                    MaterialCullMode.Back => GraphicsCullMode.Back,
                    _ => GraphicsCullMode.None
                },
                frontFaceCounterClockwise = false
            },
            depthState = new GraphicsDepthState
            {
                depthTestEnabled = isOverlayPass ? false : material.depthMode != MaterialDepthMode.Disabled,
                depthWriteEnabled = isDepthOnlyPass
                    ? true
                    : isShadowPass
                        ? true
                    : isSkyboxPass
                        ? false
                        : material.depthMode == MaterialDepthMode.ReadWrite,
                compareOp = isSkyboxPass ? GraphicsCompareOp.LessEqual : GraphicsCompareOp.LessEqual
            },
            blendState = isDepthOnlyPass
                ? new GraphicsBlendState { enabled = false }
                : isShadowPass
                    ? new GraphicsBlendState { enabled = false }
                    : CreateBlendState(material)
        });

        m_pipelineCache.Add(key, pipeline);
        return pipeline;
    }

    private IGraphicsProgram GetOrCreateProgram(string shaderName)
    {
        if (m_programCache.TryGetValue(shaderName, out var cached))
        {
            return cached;
        }

        var language = m_shaderProfile switch
        {
            "metal" => ShaderLanguage.Metal,
            "dxbc" or "dxil" => ShaderLanguage.Hlsl,
            "spirv" => ShaderLanguage.SpirV,
            _ => ShaderLanguage.Glsl
        };

        var vsBytes = LoadShader("vs", shaderName);
        var fsBytes = LoadShader("fs", shaderName);
        var vs = m_device.CreateShader(new ShaderDescription
        {
            stage = ShaderStage.Vertex,
            language = language,
            bytecode = vsBytes
        });
        var fs = m_device.CreateShader(new ShaderDescription
        {
            stage = ShaderStage.Fragment,
            language = language,
            bytecode = fsBytes
        });

        var program = m_device.CreateProgram(new GraphicsProgramDescription
        {
            shaders = [vs, fs]
        });

        m_shaderCache.Add(vs);
        m_shaderCache.Add(fs);
        m_programCache.Add(shaderName, program);
        return program;
    }

    private ReadOnlyMemory<byte> LoadShader(string stagePrefix, string shaderName)
    {
        TryCompileRuntime(shaderName);
        var filePath = Path.Combine(m_shaderAssetRoot, m_shaderProfile, $"{stagePrefix}_{shaderName}.bin");

        if (!File.Exists(filePath))
        {
            TryCompileRuntime("cubes");
            filePath = Path.Combine(m_shaderAssetRoot, m_shaderProfile, $"{stagePrefix}_cubes.bin");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Cannot find shader bytecode for '{shaderName}' in profile '{m_shaderProfile}'.", filePath);
        }

        return File.ReadAllBytes(filePath);
    }

    private void TryCompileRuntime(string shaderName)
    {
        try
        {
            RuntimeShaderCompiler.EnsureCompiled(m_shaderAssetRoot, m_shaderProfile, shaderName);
        }
        catch (FileNotFoundException)
        {
            // Allow fallback shader resolution when custom shader source is not provided.
        }
    }

    private IGraphicsInputLayout CreateInputLayout(VertexLayout vertexLayout)
    {
        var elements = vertexLayout.elements
            .Select(x => new GraphicsVertexElement
            {
                semantic = ToGraphicsSemantic(x.semantic),
                semanticIndex = x.semanticIndex,
                format = ToVertexFormat(x.sizeInBytes),
                offset = x.offset
            })
            .ToArray();

        return m_device.CreateInputLayout(new GraphicsInputLayoutDescription
        {
            elements = elements,
            stride = vertexLayout.stride
        });
    }

    private static GraphicsBlendState CreateBlendState(Material material)
    {
        if (material.surfaceType == MaterialSurfaceType.Opaque)
        {
            return new GraphicsBlendState { enabled = false };
        }

        return material.blendMode switch
        {
            MaterialBlendMode.Additive => new GraphicsBlendState
            {
                enabled = true,
                srcColorFactor = GraphicsBlendFactor.One,
                dstColorFactor = GraphicsBlendFactor.One,
                srcAlphaFactor = GraphicsBlendFactor.One,
                dstAlphaFactor = GraphicsBlendFactor.One
            },
            MaterialBlendMode.Multiply => new GraphicsBlendState
            {
                enabled = true,
                srcColorFactor = GraphicsBlendFactor.DstColor,
                dstColorFactor = GraphicsBlendFactor.Zero,
                srcAlphaFactor = GraphicsBlendFactor.One,
                dstAlphaFactor = GraphicsBlendFactor.Zero
            },
            _ => new GraphicsBlendState
            {
                enabled = true,
                srcColorFactor = GraphicsBlendFactor.SrcAlpha,
                dstColorFactor = GraphicsBlendFactor.OneMinusSrcAlpha,
                srcAlphaFactor = GraphicsBlendFactor.One,
                dstAlphaFactor = GraphicsBlendFactor.OneMinusSrcAlpha
            }
        };
    }

    private static string GetShaderName(Material material)
    {
        if (material is StandardMaterial)
        {
            return "lit";
        }

        if (material is CustomMaterial custom && !string.IsNullOrWhiteSpace(custom.shaderName))
        {
            return custom.shaderName.Trim();
        }

        return "cubes";
    }

    private IGraphicsResourceSet GetOrCreateResourceSet(Renderable renderable, Material material)
    {
        var texture = ResolveTextureForRenderable(renderable, material);
        var gpuTexture = texture is null ? m_fallbackWhiteTexture : GetOrCreateTexture(texture);
        if (m_resourceSetCache.TryGetValue(gpuTexture, out var cached))
        {
            return cached;
        }

        var resourceSet = m_device.CreateResourceSet(new ResourceSetDescription
        {
            bindings =
            [
                new GraphicsResourceBinding
                {
                    slot = 0,
                    bindingType = GraphicsBindingType.Texture,
                    resource = gpuTexture
                }
            ]
        });
        m_resourceSetCache.Add(gpuTexture, resourceSet);
        return resourceSet;
    }

    private IGraphicsTexture GetOrCreateTexture(Texture texture)
    {
        if (m_textureCache.TryGetValue(texture, out var cached))
        {
            return cached;
        }

        var width = Math.Max(1, texture.width);
        var height = Math.Max(1, texture.height);
        var description = new TextureDescription
        {
            width = width,
            height = height,
            depthOrLayers = texture is TextureCube ? 1 : 1,
            mipLevels = 1,
            dimension = texture is TextureCube ? TextureDimension.TextureCube : TextureDimension.Texture2D,
            usage = TextureUsage.Sampled,
            format = ToPixelFormat(texture.format)
        };

        var created = m_device.CreateTexture(description);
        if (description.dimension == TextureDimension.Texture2D && description.format == PixelFormat.R8G8B8A8Unorm)
        {
            created.SetData<uint>(new uint[] { 0xFFFFFFFFu });
        }

        m_textureCache.Add(texture, created);
        return created;
    }

    private IGraphicsTexture CreateFallbackWhiteTexture()
    {
        var texture = m_device.CreateTexture(new TextureDescription
        {
            width = 1,
            height = 1,
            depthOrLayers = 1,
            mipLevels = 1,
            dimension = TextureDimension.Texture2D,
            usage = TextureUsage.Sampled,
            format = PixelFormat.R8G8B8A8Unorm
        });
        texture.SetData<uint>(new uint[] { 0xFFFFFFFFu });
        return texture;
    }

    private static Texture? ResolveTextureForRenderable(Renderable renderable, Material material)
    {
        if (renderable is MeshRenderable meshRenderable
            && TryGetTextureFromPropertyBlock(meshRenderable.materialOverrides, out var overrideTexture))
        {
            return overrideTexture;
        }

        if (TryGetTextureFromPropertyBlock(material.overrides, out var materialOverrideTexture))
        {
            return materialOverrideTexture;
        }

        if (material is CustomMaterial customMaterial
            && TryGetTextureFromPropertyBlock(customMaterial.properties, out var customTexture))
        {
            return customTexture;
        }

        return material switch
        {
            StandardMaterial standard => standard.baseMap,
            UnlitMaterial unlit => unlit.colorMap,
            SpriteMaterial sprite => sprite.spriteTexture,
            SkyboxMaterial skybox => skybox.skyTexture,
            _ => null
        };
    }

    private static bool TryGetTextureFromPropertyBlock(MaterialPropertyBlock? propertyBlock, out Texture? texture)
    {
        if (propertyBlock is null)
        {
            texture = null;
            return false;
        }

        // Keep aliases compatible with common engine/importer naming conventions.
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

    private static PixelFormat ToPixelFormat(TextureFormat textureFormat)
    {
        return textureFormat switch
        {
            TextureFormat.Rgba16Float => PixelFormat.R16G16B16A16Float,
            TextureFormat.Depth24Stencil8 => PixelFormat.D24UnormS8Uint,
            TextureFormat.Depth32 => PixelFormat.D32Float,
            _ => PixelFormat.R8G8B8A8Unorm
        };
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
        var size = Math.Clamp(requestedSize, 512, 4096);
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
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var item in casterItems)
        {
            if (!TryResolveDrawable(item.renderable, out var mesh, out _, out var transform))
            {
                continue;
            }

            if (item.renderable is not MeshRenderable)
            {
                continue;
            }

            var worldBounds = ComputeWorldBounds(mesh.bounds, transform.ToMatrix());
            var worldMin = worldBounds.center - worldBounds.extents;
            var worldMax = worldBounds.center + worldBounds.extents;

            min.x = MathF.Min(min.x, worldMin.x);
            min.y = MathF.Min(min.y, worldMin.y);
            min.z = MathF.Min(min.z, worldMin.z);
            max.x = MathF.Max(max.x, worldMax.x);
            max.y = MathF.Max(max.y, worldMax.y);
            max.z = MathF.Max(max.z, worldMax.z);
            foundCasters = true;
        }

        if (!foundCasters)
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
        var shadowsEnabled = scene.settings.enableShadows;
        var shadowSettings = ResolveDirectionalShadowSettings(scene);
        if (!shadowsEnabled || !m_shadowMapReady || !shadowSettings.enabled)
        {
            Span<float> disabled = stackalloc float[4];
            disabled[0] = 0.0f;
            disabled[1] = 0.0f;
            disabled[2] = 1.0f;
            disabled[3] = 0.0f;
            m_commandList.SetGlobalVector4("u_shadowParams", disabled);
            return;
        }

        SetMatrixRows("u_lightViewProj0_", m_shadowCascades[0].viewProjection);
        SetMatrixRows("u_lightViewProj1_", m_shadowCascades[1].viewProjection);

        Span<float> v = stackalloc float[4];
        v[0] = m_shadowCascadeCount;
        v[1] = m_shadowCascades[0].splitDistance;
        v[2] = m_shadowCascades[1].splitDistance;
        v[3] = 0.0f;
        m_commandList.SetGlobalVector4("u_shadowCascadeInfo", v);

        v[0] = m_shadowCascades[0].atlasScaleBias.x;
        v[1] = m_shadowCascades[0].atlasScaleBias.y;
        v[2] = m_shadowCascades[0].atlasScaleBias.z;
        v[3] = m_shadowCascades[0].atlasScaleBias.w;
        m_commandList.SetGlobalVector4("u_shadowCascadeData0", v);

        v[0] = m_shadowCascades[1].atlasScaleBias.x;
        v[1] = m_shadowCascades[1].atlasScaleBias.y;
        v[2] = m_shadowCascades[1].atlasScaleBias.z;
        v[3] = m_shadowCascades[1].atlasScaleBias.w;
        m_commandList.SetGlobalVector4("u_shadowCascadeData1", v);

        v[0] = MathF.Max(0.0f, shadowSettings.depthBias);
        v[1] = Math.Clamp(shadowSettings.strength, 0.0f, 1.0f);
        v[2] = 1.0f / Math.Max(1, m_shadowMapSize);
        v[3] = Math.Clamp(shadowSettings.pcfRadius, 0.0f, 2.0f);
        m_commandList.SetGlobalVector4("u_shadowParams", v);
    }

    private void ApplyShadowReceiverUniform(Renderable renderable, Material material)
    {
        var receiveShadows = material.receiveShadows
            && renderable.shadowMode is ShadowMode.CastAndReceive or ShadowMode.ReceiveOnly;
        Span<float> v = stackalloc float[4];
        v[0] = receiveShadows ? 1.0f : 0.0f;
        v[1] = 0.0f;
        v[2] = 0.0f;
        v[3] = 0.0f;
        m_commandList.SetGlobalVector4("u_shadowReceiver", v);
    }

    private void SetMatrixRows(string uniformPrefix, Matrix matrix)
    {
        Span<float> row = stackalloc float[4];
        row[0] = matrix.m11;
        row[1] = matrix.m21;
        row[2] = matrix.m31;
        row[3] = matrix.m41;
        m_commandList.SetGlobalVector4($"{uniformPrefix}0", row);

        row[0] = matrix.m12;
        row[1] = matrix.m22;
        row[2] = matrix.m32;
        row[3] = matrix.m42;
        m_commandList.SetGlobalVector4($"{uniformPrefix}1", row);

        row[0] = matrix.m13;
        row[1] = matrix.m23;
        row[2] = matrix.m33;
        row[3] = matrix.m43;
        m_commandList.SetGlobalVector4($"{uniformPrefix}2", row);

        row[0] = matrix.m14;
        row[1] = matrix.m24;
        row[2] = matrix.m34;
        row[3] = matrix.m44;
        m_commandList.SetGlobalVector4($"{uniformPrefix}3", row);
    }

    private void ApplyGlobalLightUniform(RenderScene scene)
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
        m_commandList.SetGlobalVector4("u_globalLight", lightRaw);

        var lightDirection = ResolveDirectionalLightDirection(scene);
        Span<float> lightDirRaw = stackalloc float[4];
        lightDirRaw[0] = lightDirection.x;
        lightDirRaw[1] = lightDirection.y;
        lightDirRaw[2] = lightDirection.z;
        lightDirRaw[3] = 0.0f;
        m_commandList.SetGlobalVector4("u_mainLightDir", lightDirRaw);
    }

    private bool TryResolveDrawable(Renderable renderable, out Mesh mesh, out Material material, out Transform transform)
    {
        switch (renderable)
        {
            case MeshRenderable meshRenderable:
                mesh = meshRenderable.mesh;
                material = meshRenderable.material;
                transform = meshRenderable.transform;
                return true;
            case SpriteRenderable spriteRenderable:
                mesh = m_builtinQuadMesh;
                material = spriteRenderable.material;
                transform = spriteRenderable.transform;
                return true;
            case FullscreenQuadRenderable fullscreenQuadRenderable:
                mesh = m_builtinQuadMesh;
                material = fullscreenQuadRenderable.material;
                transform = fullscreenQuadRenderable.transform;
                return true;
            case SkyboxRenderable skyboxRenderable:
                mesh = m_builtinCubeMesh;
                material = skyboxRenderable.material;
                transform = skyboxRenderable.transform;
                return true;
            default:
                mesh = null!;
                material = null!;
                transform = Transform.identity;
                return false;
        }
    }

    private static string ToGraphicsSemantic(VertexSemantic semantic)
    {
        return semantic switch
        {
            VertexSemantic.Position => "POSITION",
            VertexSemantic.Normal => "NORMAL",
            VertexSemantic.Tangent => "TANGENT",
            VertexSemantic.Bitangent => "BITANGENT",
            VertexSemantic.Color0 => "COLOR",
            VertexSemantic.TexCoord0 or VertexSemantic.TexCoord1 or VertexSemantic.TexCoord2 or VertexSemantic.TexCoord3 => "TEXCOORD",
            VertexSemantic.BlendIndices => "INDICES",
            VertexSemantic.BlendWeights => "WEIGHT",
            _ => "POSITION"
        };
    }

    private static VertexFormat ToVertexFormat(int sizeInBytes)
    {
        return sizeInBytes switch
        {
            4 => VertexFormat.Float,
            8 => VertexFormat.Float2,
            12 => VertexFormat.Float3,
            16 => VertexFormat.Float4,
            _ => VertexFormat.Float4
        };
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

    private readonly record struct PipelineKey(
        string shaderName,
        MaterialSurfaceType surfaceType,
        MaterialBlendMode blendMode,
        MaterialCullMode cullMode,
        MaterialDepthMode depthMode,
        IGraphicsInputLayout inputLayout,
        RenderItemFilter filter);

    private sealed class GpuMesh : IDisposable
    {
        public GpuMesh(Mesh source, IGraphicsBuffer vertexBuffer, IGraphicsBuffer? indexBuffer, int vertexCount, int indexCount, IGraphicsInputLayout inputLayout)
        {
            this.source = source;
            this.vertexBuffer = vertexBuffer;
            this.indexBuffer = indexBuffer;
            this.vertexCount = vertexCount;
            this.indexCount = indexCount;
            this.inputLayout = inputLayout;
        }

        public Mesh source { get; }

        public IGraphicsBuffer vertexBuffer { get; }

        public IGraphicsBuffer? indexBuffer { get; }

        public int vertexCount { get; }

        public int indexCount { get; }

        public IGraphicsInputLayout inputLayout { get; }

        public void Dispose()
        {
            inputLayout.Dispose();
            indexBuffer?.Dispose();
            vertexBuffer.Dispose();
        }
    }

    private static Mesh CreateFullscreenQuadMesh()
    {
        var vertices = new[]
        {
            new StandardVertex { position = new Vector3(-1f, -1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+1f, -1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+1f, +1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-1f, +1f, 0f), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 1, 1) }
        };

        uint[] indices = [0, 1, 2, 2, 3, 0];
        return new MeshBuilder()
            .SetVertices<StandardVertex>(vertices)
            .SetIndices(indices)
            .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
            .Build("BuiltinFullscreenQuad");
    }

    private static Mesh CreateUnitCubeMesh()
    {
        const float s = 1f;
        var vertices = new[]
        {
            new StandardVertex { position = new Vector3(-s, -s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, -s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, +s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-s, +s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-s, -s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, -s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, +s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-s, +s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(1, 1, 1, 1) }
        };

        uint[] indices =
        [
            0, 1, 2, 2, 3, 0,
            1, 5, 6, 6, 2, 1,
            5, 4, 7, 7, 6, 5,
            4, 0, 3, 3, 7, 4,
            3, 2, 6, 6, 7, 3,
            4, 5, 1, 1, 0, 4
        ];

        return new MeshBuilder()
            .SetVertices<StandardVertex>(vertices)
            .SetIndices(indices)
            .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
            .Build("BuiltinUnitCube");
    }
}
