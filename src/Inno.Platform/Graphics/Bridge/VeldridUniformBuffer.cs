using System;
using Veldrid;

namespace Inno.Platform.Graphics.Bridge;

internal class VeldridUniformBuffer : IUniformBuffer
{
    private readonly GraphicsDevice m_graphicsDevice;
    internal DeviceBuffer inner { get; }
    
    public string bufferName { get; }

    public VeldridUniformBuffer(GraphicsDevice graphicsDevice, DeviceBuffer uniformBuffer, String name)
    {
        m_graphicsDevice = graphicsDevice;
        inner = uniformBuffer;
        bufferName = name;
    }
    
    public void Set<T>(ref T data) where T : unmanaged
    {
        m_graphicsDevice.UpdateBuffer(inner, 0, ref data);
    }

    public void Dispose()
    {
        inner.Dispose();
    }
    
}