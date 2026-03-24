using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public static class BgfxVertexLayoutConverter
{
    public static unsafe bgfx.VertexLayout Build(GraphicsInputLayoutDescription description)
    {
        bgfx.VertexLayout layout = default;
        var rendererType = bgfx.get_renderer_type();
        if (rendererType == bgfx.RendererType.Count)
        {
            rendererType = bgfx.RendererType.Noop;
        }

        bgfx.vertex_layout_begin(&layout, rendererType);

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

