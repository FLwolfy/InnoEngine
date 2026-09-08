using System;
using System.Collections.Generic;
using System.Threading;

using Inno.Core.Events;
using Inno.Core.Input;
using Inno.Core.Mathematics;
using Inno.Input;

namespace Inno.Adapter.Input.Sdl3;

/// <summary>
/// Accumulates SDL3-translated platform events into complete runtime input snapshots.
/// </summary>
public sealed class Sdl3InputBackend : IInputBackend
{
    private readonly HashSet<KeyCode> m_keysDown = [];
    private readonly HashSet<KeyCode> m_keysPressed = [];
    private readonly HashSet<KeyCode> m_keysReleased = [];
    private readonly HashSet<MouseButton> m_mouseButtonsDown = [];
    private readonly HashSet<MouseButton> m_mouseButtonsPressed = [];
    private readonly HashSet<MouseButton> m_mouseButtonsReleased = [];
    private readonly Lock m_sync = new();
    private readonly uint m_windowId;
    private Action<Sdl3InputBackend>? m_disposeCallback;
    private Vector2 m_mousePosition;
    private Vector2 m_mouseDelta;
    private Vector2 m_scrollDelta;
    private bool m_disposed;

    /// <summary>
    /// Creates an event-backed input adapter for one primary SDL3 window.
    /// </summary>
    /// <param name="windowId">
    /// The translated event window identifier, or zero to accept input from every window.
    /// </param>
    public Sdl3InputBackend(uint windowId)
        : this(windowId, null)
    {
    }

    internal Sdl3InputBackend(uint windowId, Action<Sdl3InputBackend>? disposeCallback)
    {
        m_windowId = windowId;
        m_disposeCallback = disposeCallback;
    }

    /// <summary>
    /// Applies one backend-neutral event translated by the owning SDL3 application.
    /// </summary>
    /// <param name="evnt">
    /// The event observed before runtime dispatch.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="evnt"/> is null.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown after this backend is disposed.
    /// </exception>
    public void ProcessEvent(Event evnt)
    {
        ArgumentNullException.ThrowIfNull(evnt);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!Accepts(evnt))
            return;
        lock (m_sync)
        {
            switch (evnt)
            {
                case KeyPressedEvent key when !key.repeat:
                    if (m_keysDown.Add(key.key))
                        m_keysPressed.Add(key.key);
                    break;
                case KeyReleasedEvent key:
                    if (m_keysDown.Remove(key.key))
                        m_keysReleased.Add(key.key);
                    break;
                case MouseButtonPressedEvent button:
                    if (m_mouseButtonsDown.Add(button.button))
                        m_mouseButtonsPressed.Add(button.button);
                    break;
                case MouseButtonReleasedEvent button:
                    if (m_mouseButtonsDown.Remove(button.button))
                        m_mouseButtonsReleased.Add(button.button);
                    break;
                case MouseMovedEvent movement:
                    var nextPosition = new Vector2(movement.x, movement.y);
                    m_mouseDelta += nextPosition - m_mousePosition;
                    m_mousePosition = nextPosition;
                    break;
                case MouseScrolledEvent scroll:
                    m_scrollDelta += new Vector2(scroll.offsetX, scroll.offsetY);
                    break;
                case WindowFocusChangedEvent { isFocused: false }:
                    m_keysReleased.UnionWith(m_keysDown);
                    m_mouseButtonsReleased.UnionWith(m_mouseButtonsDown);
                    m_keysDown.Clear();
                    m_mouseButtonsDown.Clear();
                    break;
            }
        }
    }

    /// <summary>
    /// Captures an immutable snapshot of the current observable state.
    /// </summary>
    /// <param name="frameIndex">
    /// The monotonic frame identity associated with this operation.
    /// </param>
    /// <returns>
    /// The validated input snapshot that represents the completed operation.
    /// </returns>
    public InputSnapshot Capture(long frameIndex)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        lock (m_sync)
        {
            var snapshot = new InputSnapshot(
                frameIndex,
                m_keysDown,
                m_keysPressed,
                m_keysReleased,
                m_mouseButtonsDown,
                m_mouseButtonsPressed,
                m_mouseButtonsReleased,
                m_mousePosition,
                m_mouseDelta,
                m_scrollDelta);
            m_keysPressed.Clear();
            m_keysReleased.Clear();
            m_mouseButtonsPressed.Clear();
            m_mouseButtonsReleased.Clear();
            m_mouseDelta = Vector2.ZERO;
            m_scrollDelta = Vector2.ZERO;
            return snapshot;
        }
    }

    /// <summary>
    /// Releases the resources owned by this implementation.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        lock (m_sync)
        {
            m_disposed = true;
            Action<Sdl3InputBackend>? callback = m_disposeCallback;
            m_disposeCallback = null;
            callback?.Invoke(this);
            m_keysDown.Clear();
            m_keysPressed.Clear();
            m_keysReleased.Clear();
            m_mouseButtonsDown.Clear();
            m_mouseButtonsPressed.Clear();
            m_mouseButtonsReleased.Clear();
        }
    }

    internal void DisconnectSource() => m_disposeCallback = null;

    private bool Accepts(Event evnt)
        => m_windowId == 0 || evnt switch
        {
            KeyEvent key => key.windowId == m_windowId,
            MouseEvent mouse => mouse.windowId == m_windowId,
            WindowEvent window => window.windowId == m_windowId,
            _ => false
        };
}
