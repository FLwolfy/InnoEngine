namespace Inno.Editor.Core;

/// <summary>
/// Describes one immutable editor frame.
/// </summary>
/// <param name="deltaTime">
/// The elapsed frame time in seconds.
/// </param>
/// <param name="totalTime">
/// The absolute editor runtime in seconds.
/// </param>
/// <param name="isFocused">
/// Whether an editor viewport owns application focus.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
public readonly record struct EditorFrame(
    float deltaTime,
    float totalTime,
    bool isFocused);
