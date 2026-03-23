using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public static class BgfxFormatConverter
{
    public static bgfx.TextureFormat ToBgfxTextureFormat(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R8Unorm => bgfx.TextureFormat.R8,
            PixelFormat.R8G8B8A8Unorm => bgfx.TextureFormat.RGBA8,
            PixelFormat.B8G8R8A8Unorm => bgfx.TextureFormat.BGRA8,
            PixelFormat.R16G16B16A16Float => bgfx.TextureFormat.RGBA16F,
            PixelFormat.R32G32B32A32Float => bgfx.TextureFormat.RGBA32F,
            PixelFormat.D24UnormS8Uint => bgfx.TextureFormat.D24S8,
            PixelFormat.D32Float => bgfx.TextureFormat.D32F,
            _ => bgfx.TextureFormat.BGRA8
        };
    }
}

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
                ? (ulong)bgfx.StateFlags.CullCcw
                : (ulong)bgfx.StateFlags.CullCw,
            GraphicsCullMode.Back => frontFaceCounterClockwise
                ? (ulong)bgfx.StateFlags.CullCw
                : (ulong)bgfx.StateFlags.CullCcw,
            _ => 0UL
        };

        state |= (ulong)bgfx.StateFlags.Msaa;
        return state;
    }
}

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

public static class BgfxVertexLayoutConverter
{
    public static unsafe bgfx.VertexLayout Build(GraphicsInputLayoutDescription description)
    {
        bgfx.VertexLayout layout = default;
        bgfx.vertex_layout_begin(&layout, bgfx.RendererType.Count);

        foreach (var element in description.elements)
        {
            var attrib = ParseAttrib(element.semantic, element.semanticIndex);
            GetAttribFormat(element.format, out var count, out var type, out var normalized, out var asInt);
            bgfx.vertex_layout_add(&layout, attrib, count, type, normalized, asInt);
        }

        bgfx.vertex_layout_end(&layout);
        return layout;
    }

    private static bgfx.Attrib ParseAttrib(string semantic, int semanticIndex)
    {
        var key = semantic.ToUpperInvariant();
        return key switch
        {
            "POSITION" => bgfx.Attrib.Position,
            "NORMAL" => bgfx.Attrib.Normal,
            "TANGENT" => bgfx.Attrib.Tangent,
            "BITANGENT" => bgfx.Attrib.Bitangent,
            "INDICES" => bgfx.Attrib.Indices,
            "WEIGHT" => bgfx.Attrib.Weight,
            "COLOR" => semanticIndex switch
            {
                0 => bgfx.Attrib.Color0,
                1 => bgfx.Attrib.Color1,
                2 => bgfx.Attrib.Color2,
                3 => bgfx.Attrib.Color3,
                _ => bgfx.Attrib.Color0
            },
            "TEXCOORD" => semanticIndex switch
            {
                0 => bgfx.Attrib.TexCoord0,
                1 => bgfx.Attrib.TexCoord1,
                2 => bgfx.Attrib.TexCoord2,
                3 => bgfx.Attrib.TexCoord3,
                4 => bgfx.Attrib.TexCoord4,
                5 => bgfx.Attrib.TexCoord5,
                6 => bgfx.Attrib.TexCoord6,
                7 => bgfx.Attrib.TexCoord7,
                _ => bgfx.Attrib.TexCoord0
            },
            _ => bgfx.Attrib.Position
        };
    }

    private static void GetAttribFormat(VertexFormat format, out byte count, out bgfx.AttribType type, out bool normalized, out bool asInt)
    {
        count = 0;
        type = bgfx.AttribType.Float;
        normalized = false;
        asInt = false;

        switch (format)
        {
            case VertexFormat.Float:
                count = 1;
                type = bgfx.AttribType.Float;
                break;
            case VertexFormat.Float2:
                count = 2;
                type = bgfx.AttribType.Float;
                break;
            case VertexFormat.Float3:
                count = 3;
                type = bgfx.AttribType.Float;
                break;
            case VertexFormat.Float4:
                count = 4;
                type = bgfx.AttribType.Float;
                break;
            case VertexFormat.Byte4Normalized:
                count = 4;
                type = bgfx.AttribType.Uint8;
                normalized = true;
                break;
            case VertexFormat.UShort2Normalized:
                count = 2;
                type = bgfx.AttribType.Uint16;
                normalized = true;
                break;
            case VertexFormat.UShort4Normalized:
                count = 4;
                type = bgfx.AttribType.Uint16;
                normalized = true;
                break;
            default:
                count = 4;
                type = bgfx.AttribType.Float;
                break;
        }
    }
}

public sealed class BgfxShaderBindingMap
{
}

public sealed class BgfxViewAllocator
{
    private ushort m_nextId = 1;

    public ushort Allocate()
    {
        var id = m_nextId;
        m_nextId++;
        return id;
    }
}
