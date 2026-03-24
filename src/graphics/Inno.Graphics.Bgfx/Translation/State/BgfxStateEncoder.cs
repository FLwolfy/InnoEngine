using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public static class BgfxStateEncoder
{
    public static ulong EncodeState(GraphicsRenderPipelineDescription description)
    {
        ulong state = (ulong)(bgfx.StateFlags.WriteR | bgfx.StateFlags.WriteG | bgfx.StateFlags.WriteB | bgfx.StateFlags.WriteA);

        if (description.depthState.depthTestEnabled)
        {
            state |= description.depthState.compareOp switch
            {
                GraphicsCompareOp.Never => (ulong)bgfx.StateFlags.DepthTestNever,
                GraphicsCompareOp.Less => (ulong)bgfx.StateFlags.DepthTestLess,
                GraphicsCompareOp.Equal => (ulong)bgfx.StateFlags.DepthTestEqual,
                GraphicsCompareOp.LessEqual => (ulong)bgfx.StateFlags.DepthTestLequal,
                GraphicsCompareOp.Greater => (ulong)bgfx.StateFlags.DepthTestGreater,
                GraphicsCompareOp.NotEqual => (ulong)bgfx.StateFlags.DepthTestNotequal,
                GraphicsCompareOp.GreaterEqual => (ulong)bgfx.StateFlags.DepthTestGequal,
                _ => (ulong)bgfx.StateFlags.DepthTestAlways
            };
        }

        if (description.depthState.depthWriteEnabled)
        {
            state |= (ulong)bgfx.StateFlags.WriteZ;
        }

        var frontFaceCounterClockwise = description.rasterState.frontFaceCounterClockwise;
        if (frontFaceCounterClockwise)
        {
            state |= (ulong)bgfx.StateFlags.FrontCcw;
        }

        state |= description.rasterState.cullMode switch
        {
            GraphicsCullMode.Front => frontFaceCounterClockwise
                ? (ulong)bgfx.StateFlags.CullCw
                : (ulong)bgfx.StateFlags.CullCcw,
            GraphicsCullMode.Back => frontFaceCounterClockwise
                ? (ulong)bgfx.StateFlags.CullCcw
                : (ulong)bgfx.StateFlags.CullCw,
            _ => 0UL
        };

        state |= (ulong)bgfx.StateFlags.Msaa;
        return state;
    }
}

