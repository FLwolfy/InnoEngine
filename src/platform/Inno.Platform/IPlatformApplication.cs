using System;

namespace Inno.Platform;

public interface IPlatformApplication : IDisposable
{
    IPlatformWindow CreateWindow(PlatformWindowOptions options);

    bool PollEvent(out PlatformEvent platformEvent);
}
