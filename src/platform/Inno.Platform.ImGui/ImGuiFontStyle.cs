using System;

namespace Inno.Platform.ImGui;

/// <summary>
/// Describes composable visual variants of an ImGui font family.
/// </summary>
[Flags]
public enum ImGuiFontStyle
{
    /// <summary>Uses the regular font face.</summary>
    Regular = 0,

    /// <summary>Uses a bold font face.</summary>
    Bold = 1 << 0,

    /// <summary>Uses an italic font face.</summary>
    Italic = 1 << 1
}
