using System;
using System.Collections.Generic;
using Inno.Core.Events;
using Inno.Core.Math;

namespace Inno.Platform.Display;

public interface IDisplaySystem : IDisposable
{
    int GetDisplayNumber();
    Rect GetDisplayBounds(int displayIndex);
    Rect GetUsableDisplayBounds(int displayIndex);
    
    Vector2Int GetGlobalMousePos();
    IReadOnlyList<Input.MouseButton> GetGlobalMouseButton();
    
    void ShowCursor(bool show);
    void SetCursor(Input.MouseCursor cursor);
}