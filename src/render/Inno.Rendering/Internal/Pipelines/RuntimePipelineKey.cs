using Inno.Graphics;

namespace Inno.Rendering;

internal readonly record struct RuntimePipelineKey(
    string shaderName,
    MaterialSurfaceType surfaceType,
    MaterialBlendMode blendMode,
    MaterialCullMode cullMode,
    MaterialDepthMode depthMode,
    IGraphicsInputLayout inputLayout,
    RenderItemFilter filter);
