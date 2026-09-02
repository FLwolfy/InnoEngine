using System;

namespace Inno.Core.Input;

/// <summary>
/// Identifies the supported key modifier values for this contract.
/// </summary>
[Flags]
public enum KeyModifier
{
    /// <summary>
    /// The none key.
    /// </summary>
    None = 0,
    /// <summary>
    /// The alt key.
    /// </summary>
    Alt = 1,
    /// <summary>
    /// The control key.
    /// </summary>
    Control = 2,
    /// <summary>
    /// The shift key.
    /// </summary>
    Shift = 4,
    /// <summary>
    /// The super key.
    /// </summary>
    Super = 8,
}