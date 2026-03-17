namespace Inno.Core.Events;

public enum EventType
{
    None = 0,

    // Application
    WindowClose,
    WindowResize,
        
    // Keyboard
    KeyPressed,
    KeyReleased,
        
    // Mouse
    MouseButtonPressed,
    MouseButtonReleased,
    MouseMoved,
    MouseScrolled
}