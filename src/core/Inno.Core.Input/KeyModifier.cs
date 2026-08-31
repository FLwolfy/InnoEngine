using System;

namespace Inno.Core.Input;

[Flags]
public enum KeyModifier
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Super = 8,
}