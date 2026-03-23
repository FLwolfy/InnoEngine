using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxShader : DisposableGraphicsResource, IGraphicsShader
{
    private bgfx.ShaderHandle m_handle;

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

public sealed class BgfxProgram : DisposableGraphicsResource, IGraphicsProgram
{
    private bgfx.ProgramHandle m_handle;

    public BgfxProgram(GraphicsProgramDescription description)
    {
        var vertexShader = description.shaders
            .OfType<BgfxShader>()
            .FirstOrDefault(x => x.stage == ShaderStage.Vertex);

        var fragmentShader = description.shaders
            .OfType<BgfxShader>()
            .FirstOrDefault(x => x.stage == ShaderStage.Fragment);

        if (vertexShader is null || fragmentShader is null)
        {
            throw new InvalidOperationException("bgfx program requires both vertex and fragment shaders.");
        }

        m_handle = bgfx.create_program(vertexShader.handle, fragmentShader.handle, false);
        if (!m_handle.Valid)
        {
            throw new InvalidOperationException("bgfx failed to create program from shaders.");
        }
    }

    internal bgfx.ProgramHandle handle => m_handle;

    protected override void Dispose(bool disposing)
    {
        if (m_handle.Valid)
        {
            bgfx.destroy_program(m_handle);
            m_handle = default;
        }
    }
}
