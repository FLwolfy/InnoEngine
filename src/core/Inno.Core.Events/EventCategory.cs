using System;

namespace Inno.Core.Events;

[Flags]
public enum EventCategory
{
    None = 0,
    Application = 1 << 0,
    Input       = 1 << 1,
    Keyboard    = 1 << 2,
    Mouse       = 1 << 3
}