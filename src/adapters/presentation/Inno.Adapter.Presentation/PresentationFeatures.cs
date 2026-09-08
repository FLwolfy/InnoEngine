using System;

namespace Inno.Adapter.Presentation;

/// <summary>
/// Describes optional capabilities requested from a host presentation backend.
/// </summary>
[Flags]
public enum PresentationFeatures
{
    /// <summary>
    /// Requests no optional presentation features.
    /// </summary>
    None = 0,

    /// <summary>
    /// Allows the presentation to create detached native windows.
    /// </summary>
    MultipleWindows = 1 << 0,

    /// <summary>
    /// Enables dockable host surfaces when supported.
    /// </summary>
    Docking = 1 << 1,

    /// <summary>
    /// Enables smooth rendering while native windows are being resized.
    /// </summary>
    SmoothResize = 1 << 2
}
