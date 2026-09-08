namespace Inno.Input;

/// <summary>
/// Exposes the current immutable input snapshot to runtime systems and scripts.
/// </summary>
public interface IInputService
{
    /// <summary>
    /// Gets the snapshot captured at the current frame boundary.
    /// </summary>
    InputSnapshot snapshot { get; }
}
