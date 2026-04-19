using System;
using System.Collections.Generic;
using Inno.Core.Events;

namespace Inno.Platform;

/// <summary>
/// Platform runtime entry point responsible for window creation and platform event polling.
/// </summary>
public sealed partial class PlatformApplication : IDisposable
{
    /// <summary>
    /// Initializes platform subsystems required for windowing and input events.
    /// </summary>
    public PlatformApplication()
    {
        Initialize();
    }

    /// <summary>
    /// Creates a platform window from the provided options.
    /// </summary>
    /// <param name="options">Window creation options.</param>
    /// <returns>The created <see cref="PlatformWindow"/> instance.</returns>
    public partial PlatformWindow CreateWindow(PlatformWindowOptions options);

    /// <summary>
    /// Polls the next translated platform event.
    /// </summary>
    /// <param name="evnt">The translated event, or <see langword="null"/> when no event is available.</param>
    /// <returns><see langword="true"/> when an event was returned; otherwise <see langword="false"/>.</returns>
    public partial bool PollEvent(out Event? evnt);

    /// <summary>
    /// Gets a snapshot of all currently valid platform windows.
    /// </summary>
    /// <returns>
    /// A read-only list containing every currently valid window, including windows created by integrations
    /// such as ImGui viewports.
    /// </returns>
    public partial IReadOnlyList<PlatformWindow> GetWindows();

    /// <summary>
    /// Releases all platform resources owned by this application instance.
    /// </summary>
    public partial void Dispose();
}
