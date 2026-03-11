namespace Inno.Core.Math;

/// <summary>
/// Represents an RGBA color with float components in the range [0, 1].
/// </summary>
public struct Color
{
    public float r;
    public float g;
    public float b;
    public float a;

    public Color(float r, float g, float b, float a = 1f)
    {
        this.r = MathHelper.Clamp(r, 0.0f, 1.0f);
        this.g = MathHelper.Clamp(g, 0.0f, 1.0f);
        this.b = MathHelper.Clamp(b, 0.0f, 1.0f);
        this.a = MathHelper.Clamp(a, 0.0f, 1.0f);
    }

    public static Color FromBytes(byte r, byte g, byte b, byte a = 255)
    {
        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
    }
    
    public (byte R, byte G, byte B, byte A) ToBytes()
    {
        return (
            (byte)(r * 255),
            (byte)(g * 255),
            (byte)(b * 255),
            (byte)(a * 255)
        );
    }
    
    public uint ToUInt32ARGB()
    {
        byte red = (byte)(r * 255);
        byte green = (byte)(g * 255);
        byte blue = (byte)(b * 255);
        byte alpha = (byte)(a * 255);

        return (uint)((alpha << 24) | (blue << 16) | (green << 8) | red);
    }

    
    public override string ToString() => $"Color({r:F2}, {g:F2}, {b:F2}, {a:F2})";
    
    public static readonly Color TRANSPARENT = new(0, 0, 0, 0);
    public static readonly Color WHITE = new(1, 1, 1, 1);
    public static readonly Color BLACK = new(0, 0, 0, 1);
    public static readonly Color RED = FromBytes(255, 0, 0);
    public static readonly Color GREEN = FromBytes(0, 255, 0);
    public static readonly Color BLUE = FromBytes(0, 0, 255);
    public static readonly Color YELLOW = FromBytes(255, 255, 0);
    public static readonly Color MAGENTA = FromBytes(255, 0, 255);
    public static readonly Color CYAN = FromBytes(0, 255, 255);
    public static readonly Color GRAY = FromBytes(128, 128, 128);
    public static readonly Color LIGHTGRAY = FromBytes(211, 211, 211);
    public static readonly Color DARKGRAY = FromBytes(64, 64, 64);
    public static readonly Color ORANGE = FromBytes(255, 165, 0);
    public static readonly Color PINK = FromBytes(255, 192, 203);
    public static readonly Color PURPLE = FromBytes(128, 0, 128);
    public static readonly Color BROWN = FromBytes(139, 69, 19);
    public static readonly Color CORNFLOWERBLUE = FromBytes(100, 149, 237);
    
    public static Color operator *(Color c, float factor)
    {
        return new Color(
            c.r * factor,
            c.g * factor,
            c.b * factor,
            c.a * factor
        );
    }
}