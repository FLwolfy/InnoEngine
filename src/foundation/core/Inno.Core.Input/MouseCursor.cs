namespace Inno.Core.Input;

/// <summary>
/// Identifies the supported mouse cursor values for this contract.
/// </summary>
public enum MouseCursor
{
    /// <summary>
    /// The none key.
    /// </summary>
    None = -1,
        
    /// <summary>
    /// The arrow key.
    /// </summary>
    Arrow = 0,
    /// <summary>
    /// The text input key.
    /// </summary>
    TextInput = 1,
    /// <summary>
    /// The resize all key.
    /// </summary>
    ResizeAll = 2,
    /// <summary>
    /// The resize ns key.
    /// </summary>
    ResizeNS = 3,
    /// <summary>
    /// The resize ew key.
    /// </summary>
    ResizeEW = 4,
    /// <summary>
    /// The resize nesw key.
    /// </summary>
    ResizeNESW = 5,
    /// <summary>
    /// The resize nwse key.
    /// </summary>
    ResizeNWSE = 6,
    /// <summary>
    /// The hand key.
    /// </summary>
    Hand = 7,
    /// <summary>
    /// The not allowed key.
    /// </summary>
    NotAllowed = 8,
}