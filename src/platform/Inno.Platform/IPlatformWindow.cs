using System;

namespace Inno.Platform;

public interface IPlatformWindow : IDisposable
{
    uint windowId { get; }

    string title { get; }

    int width { get; }

    int height { get; }

    bool isClosed { get; }

    PlatformNativeHandles nativeHandles { get; }

    void RequestClose();
}
