using Inno.Graphics;

namespace Inno.Rendering;

internal interface IRenderPipelineDescriptorFactory
{
    GraphicsRenderPipelineDescription Create(Material material, RenderItemFilter filter, IGraphicsProgram program, IGraphicsInputLayout inputLayout);
}
