using System;
using Inno.Core.Events;
using Inno.Core.Math;
using Inno.Platform.Graphics;

namespace Inno.Platform.Window;

[Flags]
public enum WindowFlags
{
    None         = 0,

    Hidden       = 1 << 0,
    Resizable    = 1 << 1,
    Decorated    = 1 << 2,   // true = bordered
    AlwaysOnTop  = 1 << 3,
    SkipTaskbar  = 1 << 4,
 
    ToolWindow   = 1 << 5,   // utility / inspector / palette
    Popup        = 1 << 6,   // popup / menu / tooltip
 
    AllowHighDpi = 1 << 7,
    Fullscreen   = 1 << 8,
}


public struct WindowInfo
{
    public string name;
    public int x;
    public int y;
    public int width;
    public int height;
    public WindowFlags flags;
}

public interface IWindow : IDisposable
{
    bool exists { get; }
    
    IFrameBuffer frameBuffer { get; }
    Vector2Int position { get; set; }
    Vector2Int size { get; set; }
    Rect bounds { get; }
    
    bool focused { get; }
    bool minimized { get; }
    bool resizable { get; set; }
    bool decorated { get; set; }
    string title { get; set; }

    void Show();
    void Hide();
    void Focus();
    void Close();

    event Action Resized;
    event Action Closed;
    event Action<Vector2> Moved;
    event Action FocusGained;

    void PumpEvents(EventDispatcher? dispatcher);
    EventSnapshot GetPumpedEvents();

    Vector2Int GetFrameBufferSize();
    Vector2 GetFrameBufferScale();
}
