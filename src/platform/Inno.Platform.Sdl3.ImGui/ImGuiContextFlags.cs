using System;

namespace Inno.Platform.Sdl3.ImGui;

/// <summary>
/// Identifies the supported im gui context flags values for this contract.
/// </summary>
[Flags]
public enum ImGuiContextFlags
{
    /// <summary>
    /// The none key.
    /// </summary>
    None = 0,
    /// <summary>
    /// The enable viewports key.
    /// </summary>
    EnableViewports = 1 << 0,
    /// <summary>
    /// The enable docking key.
    /// </summary>
    EnableDocking = 1 << 1,
    /// <summary>
    /// The enable smooth resize key.
    /// </summary>
    EnableSmoothResize = 1 << 2
}
