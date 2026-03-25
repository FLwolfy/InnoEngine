using Inno.Graphics;

namespace Inno.Rendering;

internal interface IMaterialParameterBinder
{
    void Bind(IGraphicsCommandList commandList, Renderable renderable, Material material);
}
