using System;
using System.Collections.Generic;

namespace Inno.Platform.Window;

public interface IWindowSystem : IDisposable
{
    IWindow mainWindow { get; }
    IEnumerable<IWindow> extraWindows { get; }

    IWindow CreateWindow(in WindowInfo info);
    void DestroyWindow(IWindow window);
    void SwapWindowBuffers(IWindow window);
}
