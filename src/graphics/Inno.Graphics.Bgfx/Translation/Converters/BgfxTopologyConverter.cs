using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public static class BgfxTopologyConverter
{
    public static ulong ToBgfxState(GraphicsPrimitiveType primitiveType)
    {
        return primitiveType switch
        {
            GraphicsPrimitiveType.TriangleStrip => (ulong)bgfx.StateFlags.PtTristrip,
            GraphicsPrimitiveType.Lines => (ulong)bgfx.StateFlags.PtLines,
            GraphicsPrimitiveType.LineStrip => (ulong)bgfx.StateFlags.PtLinestrip,
            GraphicsPrimitiveType.Points => (ulong)bgfx.StateFlags.PtPoints,
            _ => 0UL
        };
    }
}

