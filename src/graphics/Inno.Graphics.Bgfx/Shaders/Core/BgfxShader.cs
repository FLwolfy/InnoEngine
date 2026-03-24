using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxShader : DisposableGraphicsResource, IGraphicsShader
{
    private bgfx.ShaderHandle m_handle = new() { idx = ushort.MaxValue };

    public BgfxShader(ShaderDescription description)
    {
        stage = description.stage;
        bytecode = description.bytecode.ToArray();
        CreateNativeHandle();
    }

    public ShaderStage stage { get; }

    public ReadOnlyMemory<byte> bytecode { get; }

    internal bgfx.ShaderHandle handle => m_handle;

    private unsafe void CreateNativeHandle()
    {
        var bytes = bytecode.Span;
        fixed (byte* data = bytes)
        {
            var mem = bgfx.copy(data, (uint)bytes.Length);
            m_handle = bgfx.create_shader(mem);
            if (!m_handle.Valid)
            {
                throw new InvalidOperationException($"bgfx failed to create {stage} shader.");
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (m_handle.Valid)
        {
            bgfx.destroy_shader(m_handle);
            m_handle = default;
        }
    }
}

