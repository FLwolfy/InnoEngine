using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class GpuResourceRegistry : IDisposable
{
    private readonly IGraphicsDevice m_device;
    private readonly IGraphicsSwapchain m_swapchain;
    private readonly Dictionary<Mesh, RuntimeGpuMesh> m_meshCache = new();
    private readonly Dictionary<Texture, IGraphicsTexture> m_textureCache = new();
    private readonly Dictionary<(IGraphicsTexture texture, int slot), IGraphicsResourceSet> m_resourceSetCache = new();
    private readonly Dictionary<RenderTarget, IGraphicsRenderTarget> m_renderTargetCache = new();
    private IGraphicsRenderTarget? m_backbufferTarget;

    public GpuResourceRegistry(IGraphicsDevice device, IGraphicsSwapchain swapchain)
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_swapchain = swapchain ?? throw new ArgumentNullException(nameof(swapchain));
        fallbackWhiteTexture = CreateFallbackWhiteTexture();
    }

    public IGraphicsTexture fallbackWhiteTexture { get; }

    public IGraphicsRenderTarget GetOrCreateRenderTarget(RenderTarget target)
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

        var colorFormat = m_swapchain.colorFormat;
        PixelFormat? depthFormat = m_swapchain.depthFormat;
        if (target is TextureRenderTarget textureTarget)
        {
            colorFormat = Map(textureTarget.descriptor.colorFormat);
            depthFormat = textureTarget.descriptor.hasDepth ? PixelFormat.D24UnormS8Uint : null;
        }

        var created = m_device.CreateRenderTarget(new GraphicsRenderTargetDescription
        {
            width = Math.Max(1, target.width),
            height = Math.Max(1, target.height),
            colorFormats = [colorFormat],
            depthFormat = depthFormat
        });
        m_renderTargetCache.Add(target, created);
        return created;
    }

    public RuntimeGpuMesh GetOrCreateMesh(Mesh mesh)
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
        var created = new RuntimeGpuMesh(mesh, vertexBuffer, indexBuffer, mesh.vertexCount, indexCount, inputLayout);
        m_meshCache.Add(mesh, created);
        return created;
    }

    public IGraphicsTexture GetOrCreateTexture(Texture texture)
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

    public IGraphicsResourceSet GetOrCreateTextureResourceSet(IGraphicsTexture texture, int slot)
    {
        var key = (texture, slot);
        if (m_resourceSetCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resourceSet = m_device.CreateResourceSet(new ResourceSetDescription
        {
            bindings =
            [
                new GraphicsResourceBinding
                {
                    slot = slot,
                    bindingType = GraphicsBindingType.Texture,
                    resource = texture
                }
            ]
        });
        m_resourceSetCache.Add(key, resourceSet);
        return resourceSet;
    }

    public void Dispose()
    {
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
        m_backbufferTarget?.Dispose();
        fallbackWhiteTexture.Dispose();
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

    private static PixelFormat Map(RenderTargetFormat format)
    {
        return format switch
        {
            RenderTargetFormat.Rgba16Float => PixelFormat.R16G16B16A16Float,
            RenderTargetFormat.Depth24Stencil8 => PixelFormat.D24UnormS8Uint,
            RenderTargetFormat.Depth32 => PixelFormat.D32Float,
            _ => PixelFormat.R8G8B8A8Unorm
        };
    }
}
