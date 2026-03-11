using System;
using System.Runtime.InteropServices;

using Inno.Platform.Window.Bridge;

using Veldrid;
using Veldrid.StartupUtilities;
using VeldridGraphicsBackend = Veldrid.GraphicsBackend;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridGraphicsDevice : IGraphicsDevice
{
    private readonly GraphicsDevice m_graphicsDevice;
    internal GraphicsDevice inner => m_graphicsDevice;

    public GraphicsBackend backend { get; }
    public IFrameBuffer swapchainFrameBuffer { get; }

    public VeldridGraphicsDevice(VeldridSdl2Window window, GraphicsBackend backend)
    {
        var deviceOptions = new GraphicsDeviceOptions(
            debug: true,
            null,
            syncToVerticalBlank: false,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true
        );

        this.backend = backend;
        m_graphicsDevice = VeldridStartup.CreateGraphicsDevice(window.inner, deviceOptions, ToVeldridGraphicsBackend(backend));
        swapchainFrameBuffer = new VeldridFrameBuffer(m_graphicsDevice, m_graphicsDevice.SwapchainFramebuffer);

        // Ensure swapchain/backbuffer matches drawable pixel size on HiDPI displays.
        {
            var size = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
            if (size != window.size)
            {
                swapchainFrameBuffer.Resize(size.x, size.y);
            }
        }
        
        window.inner.Resized += () =>
        {
            var size = VeldridSdl2HiDpi.GetFramebufferSize(window.inner);
            swapchainFrameBuffer.Resize(size.x, size.y);
        };
    }

    private VeldridGraphicsBackend ToVeldridGraphicsBackend(GraphicsBackend graphicsBackend)
    {
        return graphicsBackend switch
        {
            GraphicsBackend.Vulkan => VeldridGraphicsBackend.Vulkan,
            GraphicsBackend.Direct3D11 => VeldridGraphicsBackend.Direct3D11,
            GraphicsBackend.OpenGL => VeldridGraphicsBackend.OpenGL,
            GraphicsBackend.Metal => VeldridGraphicsBackend.Metal,
            GraphicsBackend.OpenGLES => VeldridGraphicsBackend.OpenGLES,
            
            _ => throw new NotSupportedException($"Graphics backend {graphicsBackend} not supported")
        };
    }

    public IVertexBuffer CreateVertexBuffer(uint sizeInBytes)
    {
        var vb = m_graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.VertexBuffer));
        return new VeldridVertexBuffer(m_graphicsDevice, vb);
    }

    public IIndexBuffer CreateIndexBuffer(uint sizeInBytes)
    {
        var ib = m_graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.IndexBuffer));
        return new VeldridIndexBuffer(m_graphicsDevice, ib);
    }

    public IUniformBuffer CreateUniformBuffer(string name, Type type)
    {
        int size = Marshal.SizeOf(type);
        var ub = m_graphicsDevice.ResourceFactory.CreateBuffer(new BufferDescription((uint)size, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        return new VeldridUniformBuffer(m_graphicsDevice, ub, name);
    }

    public IFrameBuffer CreateFrameBuffer(FrameBufferDescription desc)
    {
        return new VeldridFrameBuffer(m_graphicsDevice, desc);
    }

    public IResourceSet CreateResourceSet(ResourceSetBinding binding)
    {
        return new VeldridResourceSet(m_graphicsDevice, binding);
    }
    
    public (IShader, IShader) CreateVertexFragmentShader(ShaderDescription vertDesc, ShaderDescription fragDesc)
    {
        return VeldridShader.CreateVertexFragment(m_graphicsDevice, vertDesc, fragDesc);
    }

    public IShader CreateComputeShader(ShaderDescription desc)
    {
        return VeldridShader.CreateCompute(m_graphicsDevice, desc);
    }

    public ITexture CreateTexture(TextureDescription desc)
    {
        return VeldridTexture.Create(m_graphicsDevice, desc);
    }
    
    public ISampler CreateSampler(SamplerDescription desc)
    {
        return VeldridSampler.Create(m_graphicsDevice, desc);
    }

    public IPipelineState CreatePipelineState(PipelineStateDescription desc)
    {
        return new VeldridPipelineState(m_graphicsDevice, desc);
    }

    public ICommandList CreateCommandList()
    {
        var cmdList = m_graphicsDevice.ResourceFactory.CreateCommandList();
        return new VeldridCommandList(cmdList);
    }

    public void Submit(ICommandList commandList)
    {
        if (commandList is VeldridCommandList veldridCmd)
        {
            m_graphicsDevice.SubmitCommands(veldridCmd.inner);
            m_graphicsDevice.WaitForIdle();  
        }
    }

    public void Dispose()
    {
        swapchainFrameBuffer.Dispose();
        m_graphicsDevice.Dispose();
    }
}
