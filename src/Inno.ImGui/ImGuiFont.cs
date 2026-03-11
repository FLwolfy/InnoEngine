namespace Inno.ImGui;

/// <summary>
/// Identifies the style of font used.
/// </summary>
public enum ImGuiFontStyle
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
    
    Icon
}

/// <summary>
/// Identifies the size of the font used.
/// </summary>
public enum ImGuiFontSize
{
    // Tips
    Micro   = 4,
    Tiny    = 8,
    Small   = 12,

    // Text
    Medium  = 16,
    Large   = 24,
    Huge    = 32,
    
    // Titles
    Massive = 48
}

public readonly struct ImGuiAlias(ImGuiFontStyle style, float size)
{
    public readonly ImGuiFontStyle style = style;
    public readonly float size = size;
}