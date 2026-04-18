using System;

namespace Inno.Platform.ImGui;

[Flags]
public enum ImGuiContextFlags
{
    None = 0,
    EnableViewports = 1 << 0,
    EnableDocking = 1 << 1,
    EnableSmoothResize = 1 << 2
}
