using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxRenderPipeline : DisposableGraphicsResource, IGraphicsRenderPipeline
{
    public BgfxRenderPipeline(GraphicsRenderPipelineDescription description)
    {
        this.description = description;
        program = description.program as BgfxProgram
            ?? throw new ArgumentException("Pipeline requires BgfxProgram.", nameof(description));
        inputLayout = description.inputLayout as BgfxInputLayout
            ?? throw new ArgumentException("Pipeline requires BgfxInputLayout.", nameof(description));
        state = BgfxStateEncoder.EncodeState(description);
    }

    public GraphicsRenderPipelineDescription description { get; }

    internal BgfxProgram program { get; }

    internal BgfxInputLayout inputLayout { get; }

    internal ulong state { get; }
}
