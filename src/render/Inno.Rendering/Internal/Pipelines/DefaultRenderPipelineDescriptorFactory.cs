using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class DefaultRenderPipelineDescriptorFactory : IRenderPipelineDescriptorFactory
{
    public GraphicsRenderPipelineDescription Create(Material material, RenderItemFilter filter, IGraphicsProgram program, IGraphicsInputLayout inputLayout)
    {
        var isDepthOnlyPass = filter == RenderItemFilter.DepthOnly;
        var isShadowPass = filter == RenderItemFilter.ShadowCasters;
        var isOverlayPass = filter is RenderItemFilter.Ui or RenderItemFilter.Gizmo or RenderItemFilter.PostProcess;
        var isSkyboxPass = filter == RenderItemFilter.Skybox;

        return new GraphicsRenderPipelineDescription
        {
            program = program,
            inputLayout = inputLayout,
            rasterState = new GraphicsRasterState
            {
                cullMode = isSkyboxPass
                    ? GraphicsCullMode.None
                    : isShadowPass
                        ? GraphicsCullMode.None
                        : material.cullMode switch
                        {
                            MaterialCullMode.Front => GraphicsCullMode.Front,
                            MaterialCullMode.Back => GraphicsCullMode.Back,
                            _ => GraphicsCullMode.None
                        },
                frontFaceCounterClockwise = false
            },
            depthState = new GraphicsDepthState
            {
                depthTestEnabled = isOverlayPass ? false : material.depthMode != MaterialDepthMode.Disabled,
                depthWriteEnabled = isDepthOnlyPass
                    ? true
                    : isShadowPass
                        ? true
                        : isSkyboxPass
                            ? false
                            : material.depthMode == MaterialDepthMode.ReadWrite,
                compareOp = GraphicsCompareOp.LessEqual
            },
            blendState = isDepthOnlyPass
                ? new GraphicsBlendState { enabled = false }
                : isShadowPass
                    ? new GraphicsBlendState { enabled = false }
                    : CreateBlendState(material)
        };
    }

    private static GraphicsBlendState CreateBlendState(Material material)
    {
        if (material.surfaceType == MaterialSurfaceType.Opaque)
        {
            return new GraphicsBlendState { enabled = false };
        }

        return material.blendMode switch
        {
            MaterialBlendMode.Additive => new GraphicsBlendState
            {
                enabled = true,
                srcColorFactor = GraphicsBlendFactor.One,
                dstColorFactor = GraphicsBlendFactor.One,
                srcAlphaFactor = GraphicsBlendFactor.One,
                dstAlphaFactor = GraphicsBlendFactor.One
            },
            MaterialBlendMode.Multiply => new GraphicsBlendState
            {
                enabled = true,
                srcColorFactor = GraphicsBlendFactor.DstColor,
                dstColorFactor = GraphicsBlendFactor.Zero,
                srcAlphaFactor = GraphicsBlendFactor.One,
                dstAlphaFactor = GraphicsBlendFactor.Zero
            },
            _ => new GraphicsBlendState
            {
                enabled = true,
                srcColorFactor = GraphicsBlendFactor.SrcAlpha,
                dstColorFactor = GraphicsBlendFactor.OneMinusSrcAlpha,
                srcAlphaFactor = GraphicsBlendFactor.One,
                dstAlphaFactor = GraphicsBlendFactor.OneMinusSrcAlpha
            }
        };
    }
}
