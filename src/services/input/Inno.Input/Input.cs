using Inno.Core.Execution;
using System;
using System.Threading;

using Inno.Core.Input;
using Inno.Core.Mathematics;

namespace Inno.Input;

/// <summary>
/// Binds one input service to the current asynchronous execution context.
/// </summary>
public static class InputExecutionContext
{
    private static readonly ExecutionSlot<IInputService> S_CURRENT_SCOPE = new("input");

    /// <summary>
    /// Gets the input service bound to the current execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no input scope is active.
    /// </exception>
    public static IInputService current
        => S_CURRENT_SCOPE.current;

    /// <summary>
    /// Binds an input service until the returned strict last-in-first-out scope is disposed.
    /// </summary>
    /// <param name="input">
    /// The host-owned input service.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope.
    /// </returns>
    public static IDisposable EnterScope(IInputService input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return S_CURRENT_SCOPE.Enter(input);
    }

}

/// <summary>
/// Provides script-friendly access to the current frame's input snapshot.
/// </summary>
public static class Input
{
    /// <summary>
    /// Gets the complete immutable current-frame snapshot.
    /// </summary>
    public static InputSnapshot snapshot => InputExecutionContext.current.snapshot;

    /// <summary>
    /// Gets the current pointer position in window coordinates.
    /// </summary>
    public static Vector2 mousePosition => snapshot.mousePosition;

    /// <summary>
    /// Gets pointer movement accumulated during this frame.
    /// </summary>
    public static Vector2 mouseDelta => snapshot.mouseDelta;

    /// <summary>
    /// Gets wheel movement accumulated during this frame.
    /// </summary>
    public static Vector2 scrollDelta => snapshot.scrollDelta;

    /// <summary>
    /// Determines whether a key is currently held.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key is held.
    /// </returns>
    public static bool IsKeyDown(KeyCode key) => snapshot.IsKeyDown(key);

    /// <summary>
    /// Determines whether a key was newly pressed this frame.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key transitioned to down.
    /// </returns>
    public static bool WasKeyPressed(KeyCode key) => snapshot.WasKeyPressed(key);

    /// <summary>
    /// Determines whether a key was newly released this frame.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key transitioned to up.
    /// </returns>
    public static bool WasKeyReleased(KeyCode key) => snapshot.WasKeyReleased(key);

    /// <summary>
    /// Determines whether a pointer button is currently held.
    /// </summary>
    /// <param name="button">
    /// The backend-neutral pointer button.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button is held.
    /// </returns>
    public static bool IsMouseButtonDown(MouseButton button) => snapshot.IsMouseButtonDown(button);

    /// <summary>
    /// Determines whether a pointer button was newly pressed this frame.
    /// </summary>
    /// <param name="button">
    /// The backend-neutral pointer button.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button transitioned to down.
    /// </returns>
    public static bool WasMouseButtonPressed(MouseButton button) => snapshot.WasMouseButtonPressed(button);

    /// <summary>
    /// Determines whether a pointer button was newly released this frame.
    /// </summary>
    /// <param name="button">
    /// The backend-neutral pointer button.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button transitioned to up.
    /// </returns>
    public static bool WasMouseButtonReleased(MouseButton button) => snapshot.WasMouseButtonReleased(button);
}
