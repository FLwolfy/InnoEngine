using Inno.Core.Mathematics;
using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class GraphicsRenderRuntime : IDisposable
{
    private readonly IGraphicsDevice m_device;
    private readonly IGraphicsSwapchain m_swapchain;
    private readonly IGraphicsCommandList m_commandList;
    private readonly string m_shaderProfile;
    private readonly string m_shaderAssetRoot;
    private readonly Dictionary<Mesh, GpuMesh> m_meshCache = new();
    private readonly Dictionary<string, IGraphicsProgram> m_programCache = new(StringComparer.Ordinal);
    private readonly Dictionary<PipelineKey, IGraphicsRenderPipeline> m_pipelineCache = new();
    private readonly List<IGraphicsShader> m_shaderCache = [];
    private IGraphicsRenderTarget? m_backbufferTarget;
    private bool m_frameActive;
    private bool m_passActive;

    public GraphicsRenderRuntime(IGraphicsDevice device, IGraphicsSwapchain swapchain, string? shaderProfile = null, string? shaderAssetRoot = null)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_swapchain = swapchain ?? throw new ArgumentNullException(nameof(swapchain));
        m_commandList = m_device.CreateCommandList();
        m_shaderProfile = string.IsNullOrWhiteSpace(shaderProfile) ? GetDefaultShaderProfile() : shaderProfile;
        m_shaderAssetRoot = string.IsNullOrWhiteSpace(shaderAssetRoot)
            ? Path.Combine(AppContext.BaseDirectory, "Assets")
            : shaderAssetRoot;
    }

    public void BeginFrame(RenderRequest request)
    {
        if (m_frameActive)
        {
            throw new InvalidOperationException("Render frame is already active.");
        }

        var target = GetOrCreateRenderTarget(request.target);
        var viewport = request.view.viewport;
        if (viewport.width <= 1 && viewport.height <= 1)
        {
            viewport = new Viewport(0, 0, target.width, target.height);
        }

        var clear = request.view.clear;
        var clearValue = new ClearValue(clear.color.r, clear.color.g, clear.color.b, clear.color.a, clear.depth, clear.stencil);

        m_device.BeginFrame();
        m_commandList.Begin();
        m_commandList.BeginRenderPass(target, clearValue);
        m_commandList.SetViewport(new GraphicsViewport(viewport.x, viewport.y, viewport.width, viewport.height));
        m_frameActive = true;
        m_passActive = true;
    }

    public void ExecutePass(RenderPipelineContext context, RenderList renderList, RenderItemFilter filter)
    {
        if (!m_frameActive || !m_passActive)
        {
            return;
        }

        if (filter is not (RenderItemFilter.Opaque or RenderItemFilter.Transparent))
        {
            return;
        }

        var view = context.request.view;
        var target = context.request.target;

        var aspectRatio = GetAspectRatio(view, target);
        var viewMatrix = view.camera.GetViewMatrix();
        var projectionMatrix = view.camera.GetProjectionMatrix(aspectRatio);
        Span<float> viewRaw = stackalloc float[16];
        Span<float> projRaw = stackalloc float[16];
        WriteColumnMajor(viewMatrix, viewRaw);
        WriteColumnMajor(projectionMatrix, projRaw);
        m_commandList.SetViewProjection(viewRaw, projRaw);

        foreach (var item in renderList.items)
        {
            if (item.renderable is not MeshRenderable meshRenderable)
            {
                continue;
            }

            var gpuMesh = GetOrCreateMesh(meshRenderable.mesh);
            var pipeline = GetOrCreatePipeline(meshRenderable.material, gpuMesh.inputLayout);

            m_commandList.SetPipeline(pipeline);
            m_commandList.SetVertexBuffer(gpuMesh.vertexBuffer);

            Span<float> modelRaw = stackalloc float[16];
            WriteColumnMajor(meshRenderable.transform.ToMatrix(), modelRaw);
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

        m_backbufferTarget?.Dispose();
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
                    depthFormat = m_swapchain.depthFormat
                });
            }

            return m_backbufferTarget;
        }

        return m_backbufferTarget ??= m_device.CreateRenderTarget(new GraphicsRenderTargetDescription
        {
            width = Math.Max(1, target.width),
            height = Math.Max(1, target.height),
            colorFormats = [m_swapchain.colorFormat],
            depthFormat = m_swapchain.depthFormat
        });
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

    private IGraphicsRenderPipeline GetOrCreatePipeline(Material material, IGraphicsInputLayout inputLayout)
    {
        var shaderName = GetShaderName(material);
        var key = new PipelineKey(shaderName, material.surfaceType, material.blendMode, material.cullMode, material.depthMode, inputLayout);
        if (m_pipelineCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var program = GetOrCreateProgram(shaderName);
        var pipeline = m_device.CreateRenderPipeline(new GraphicsRenderPipelineDescription
        {
            program = program,
            inputLayout = inputLayout,
            rasterState = new GraphicsRasterState
            {
                cullMode = material.cullMode switch
                {
                    MaterialCullMode.Front => GraphicsCullMode.Front,
                    MaterialCullMode.Back => GraphicsCullMode.Back,
                    _ => GraphicsCullMode.None
                }
            },
            depthState = new GraphicsDepthState
            {
                depthTestEnabled = material.depthMode != MaterialDepthMode.Disabled,
                depthWriteEnabled = material.depthMode == MaterialDepthMode.ReadWrite,
                compareOp = GraphicsCompareOp.LessEqual
            },
            blendState = CreateBlendState(material)
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
        var filePath = Path.Combine(m_shaderAssetRoot, m_shaderProfile, $"{stagePrefix}_{shaderName}.bin");
        if (!File.Exists(filePath))
        {
            filePath = Path.Combine(m_shaderAssetRoot, m_shaderProfile, $"{stagePrefix}_cubes.bin");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Cannot find shader bytecode for '{shaderName}' in profile '{m_shaderProfile}'.", filePath);
        }

        return File.ReadAllBytes(filePath);
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
        if (material is CustomMaterial custom && !string.IsNullOrWhiteSpace(custom.shaderName))
        {
            return custom.shaderName.Trim();
        }

        return "cubes";
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
        IGraphicsInputLayout inputLayout);

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
}
