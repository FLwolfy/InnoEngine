using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Input;
using Inno.Core.Mathematics;

namespace Inno.Input;

/// <summary>
/// Captures immutable keyboard and pointer state at one runtime frame boundary.
/// </summary>
public sealed class InputSnapshot
{
    private readonly HashSet<KeyCode> m_keysDown;
    private readonly HashSet<KeyCode> m_keysPressed;
    private readonly HashSet<KeyCode> m_keysReleased;
    private readonly HashSet<MouseButton> m_mouseButtonsDown;
    private readonly HashSet<MouseButton> m_mouseButtonsPressed;
    private readonly HashSet<MouseButton> m_mouseButtonsReleased;

    /// <summary>
    /// Creates a complete immutable input snapshot.
    /// </summary>
    /// <param name="frameIndex">
    /// The runtime frame that owns the snapshot.
    /// </param>
    /// <param name="keysDown">
    /// Keys held at the frame boundary.
    /// </param>
    /// <param name="keysPressed">
    /// Keys newly pressed since the previous snapshot.
    /// </param>
    /// <param name="keysReleased">
    /// Keys newly released since the previous snapshot.
    /// </param>
    /// <param name="mouseButtonsDown">
    /// Pointer buttons held at the frame boundary.
    /// </param>
    /// <param name="mouseButtonsPressed">
    /// Pointer buttons newly pressed since the previous snapshot.
    /// </param>
    /// <param name="mouseButtonsReleased">
    /// Pointer buttons newly released since the previous snapshot.
    /// </param>
    /// <param name="mousePosition">
    /// Current pointer position in window coordinates.
    /// </param>
    /// <param name="mouseDelta">
    /// Pointer movement accumulated since the previous snapshot.
    /// </param>
    /// <param name="scrollDelta">
    /// Wheel movement accumulated since the previous snapshot.
    /// </param>
    public InputSnapshot(
        long frameIndex,
        IEnumerable<KeyCode>? keysDown = null,
        IEnumerable<KeyCode>? keysPressed = null,
        IEnumerable<KeyCode>? keysReleased = null,
        IEnumerable<MouseButton>? mouseButtonsDown = null,
        IEnumerable<MouseButton>? mouseButtonsPressed = null,
        IEnumerable<MouseButton>? mouseButtonsReleased = null,
        Vector2 mousePosition = default,
        Vector2 mouseDelta = default,
        Vector2 scrollDelta = default)
    {
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        this.frameIndex = frameIndex;
        m_keysDown = Copy(keysDown);
        m_keysPressed = Copy(keysPressed);
        m_keysReleased = Copy(keysReleased);
        m_mouseButtonsDown = Copy(mouseButtonsDown);
        m_mouseButtonsPressed = Copy(mouseButtonsPressed);
        m_mouseButtonsReleased = Copy(mouseButtonsReleased);
        this.mousePosition = mousePosition;
        this.mouseDelta = mouseDelta;
        this.scrollDelta = scrollDelta;
    }

    /// <summary>
    /// Gets an empty initial snapshot.
    /// </summary>
    public static InputSnapshot empty { get; } = new(0);

    /// <summary>
    /// Gets the runtime frame that owns this snapshot.
    /// </summary>
    public long frameIndex { get; }

    /// <summary>
    /// Gets the current pointer position in window coordinates.
    /// </summary>
    public Vector2 mousePosition { get; }

    /// <summary>
    /// Gets pointer movement accumulated since the previous snapshot.
    /// </summary>
    public Vector2 mouseDelta { get; }

    /// <summary>
    /// Gets wheel movement accumulated since the previous snapshot.
    /// </summary>
    public Vector2 scrollDelta { get; }

    /// <summary>
    /// Determines whether a key is currently held.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key is held.
    /// </returns>
    public bool IsKeyDown(KeyCode key) => m_keysDown.Contains(key);

    /// <summary>
    /// Determines whether a key was newly pressed during this frame.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key transitioned to down.
    /// </returns>
    public bool WasKeyPressed(KeyCode key) => m_keysPressed.Contains(key);

    /// <summary>
    /// Determines whether a key was newly released during this frame.
    /// </summary>
    /// <param name="key">
    /// The backend-neutral physical key.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the key transitioned to up.
    /// </returns>
    public bool WasKeyReleased(KeyCode key) => m_keysReleased.Contains(key);

    /// <summary>
    /// Determines whether a pointer button is currently held.
    /// </summary>
    /// <param name="button">
    /// The backend-neutral pointer button.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button is held.
    /// </returns>
    public bool IsMouseButtonDown(MouseButton button) => m_mouseButtonsDown.Contains(button);

    /// <summary>
    /// Determines whether a pointer button was newly pressed during this frame.
    /// </summary>
    /// <param name="button">
    /// The backend-neutral pointer button.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button transitioned to down.
    /// </returns>
    public bool WasMouseButtonPressed(MouseButton button) => m_mouseButtonsPressed.Contains(button);

    /// <summary>
    /// Determines whether a pointer button was newly released during this frame.
    /// </summary>
    /// <param name="button">
    /// The backend-neutral pointer button.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button transitioned to up.
    /// </returns>
    public bool WasMouseButtonReleased(MouseButton button) => m_mouseButtonsReleased.Contains(button);

    private static HashSet<TValue> Copy<TValue>(IEnumerable<TValue>? values)
        where TValue : struct, Enum
        => values is null ? [] : values.ToHashSet();
}
