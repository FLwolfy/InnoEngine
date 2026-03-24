using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxProgram : DisposableGraphicsResource, IGraphicsProgram
{
    private bgfx.ProgramHandle m_handle = new() { idx = ushort.MaxValue };

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
