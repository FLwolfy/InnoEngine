using System;
using System.Collections.Generic;

using Inno.Core.Input;
using Inno.Core.Mathematics;

namespace Inno.Platform.Display;

public interface IDisplaySystem : IDisposable
{
    // Display Info
    int GetDisplayNumber();
    Rect GetDisplayBounds(int displayIndex);
    Rect GetUsableDisplayBounds(int displayIndex);
    
    // Display Input
    Vector2Int GetGlobalMousePos();
    IReadOnlyList<MouseButton> GetGlobalMouseButton();
    
    void ShowCursor(bool show);
    void SetCursor(MouseCursor cursor);
    
    // Display Window
    IWindow mainWindow { get; }
    IEnumerable<IWindow> extraWindows { get; }

    IWindow CreateWindow(in WindowInfo info);
    void DestroyWindow(IWindow window);
    void SwapWindowBuffers(IWindow window);
}