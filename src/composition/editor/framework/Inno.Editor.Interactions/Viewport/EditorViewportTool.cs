using System;
using Inno.Core.Input;
using Inno.Core.Mathematics;

namespace Inno.Editor.Interactions;

/// <summary>
/// Selects the lifecycle phase of one viewport pointer sample.
/// </summary>
public enum EditorViewportPointerPhase
{
    /// <summary>
    /// The pointer button began a gesture.
    /// </summary>
    Down,
    /// <summary>
    /// The pointer moved or changed hover position.
    /// </summary>
    Move,
    /// <summary>
    /// The pointer button ended a gesture.
    /// </summary>
    Up,
    /// <summary>
    /// The platform cancelled the gesture.
    /// </summary>
    Cancel
}

/// <summary>
/// Selects a presentation-independent cursor requested by a viewport tool.
/// </summary>
public enum EditorViewportCursor
{
    /// <summary>
    /// Uses the normal arrow cursor.
    /// </summary>
    Arrow,
    /// <summary>
    /// Uses a precise crosshair.
    /// </summary>
    Crosshair,
    /// <summary>
    /// Uses a hand suitable for panning.
    /// </summary>
    Hand,
    /// <summary>
    /// Uses four-direction movement arrows.
    /// </summary>
    Move,
    /// <summary>
    /// Uses horizontal resizing arrows.
    /// </summary>
    ResizeHorizontal,
    /// <summary>
    /// Uses vertical resizing arrows.
    /// </summary>
    ResizeVertical,
    /// <summary>
    /// Hides the pointer while the tool owns it.
    /// </summary>
    Hidden
}

/// <summary>
/// Contains one presentation-independent shortcut sample for a focused viewport tool.
/// </summary>
public readonly record struct EditorViewportShortcut
{
    /// <summary>
    /// Creates one viewport shortcut sample.
    /// </summary>
    /// <param name="key">
    /// Pressed key.
    /// </param>
    /// <param name="modifiers">
    /// Active keyboard modifiers.
    /// </param>
    /// <param name="repeat">
    /// Whether the platform generated an auto-repeat press.
    /// </param>
    public EditorViewportShortcut(KeyCode key, KeyModifier modifiers = KeyModifier.None, bool repeat = false)
    {
        this.key = key;
        this.modifiers = modifiers;
        this.repeat = repeat;
    }

    /// <summary>
    /// Gets the pressed key.
    /// </summary>
    public KeyCode key { get; }

    /// <summary>
    /// Gets active keyboard modifiers.
    /// </summary>
    public KeyModifier modifiers { get; }

    /// <summary>
    /// Gets whether the shortcut is an auto-repeat press.
    /// </summary>
    public bool repeat { get; }
}

/// <summary>
/// Contains one immutable viewport pointer sample in screen and world coordinates.
/// </summary>
public readonly record struct EditorViewportPointerEvent
{
    /// <summary>
    /// Creates one viewport pointer sample.
    /// </summary>
    /// <param name="pointerId">
    /// Platform pointer identity.
    /// </param>
    /// <param name="phase">
    /// Gesture lifecycle phase.
    /// </param>
    /// <param name="screenPosition">
    /// Viewport-local pixel position.
    /// </param>
    /// <param name="worldPosition">
    /// Corresponding world position.
    /// </param>
    /// <param name="button">
    /// Platform button number.
    /// </param>
    /// <param name="modifiers">
    /// Active keyboard modifiers.
    /// </param>
    public EditorViewportPointerEvent(
        int pointerId,
        EditorViewportPointerPhase phase,
        Vector2 screenPosition,
        Vector2 worldPosition,
        int button,
        KeyModifier modifiers)
    {
        this.pointerId = pointerId;
        this.phase = phase;
        this.screenPosition = screenPosition;
        this.worldPosition = worldPosition;
        this.button = button;
        this.modifiers = modifiers;
    }

    /// <summary>
    /// Gets the platform pointer identity.
    /// </summary>
    public int pointerId { get; }

    /// <summary>
    /// Gets the gesture lifecycle phase.
    /// </summary>
    public EditorViewportPointerPhase phase { get; }

    /// <summary>
    /// Gets the viewport-local pixel position.
    /// </summary>
    public Vector2 screenPosition { get; }

    /// <summary>
    /// Gets the corresponding world position.
    /// </summary>
    public Vector2 worldPosition { get; }

    /// <summary>
    /// Gets the platform button number.
    /// </summary>
    public int button { get; }

    /// <summary>
    /// Gets active keyboard modifiers.
    /// </summary>
    public KeyModifier modifiers { get; }
}

/// <summary>
/// Converts coordinates for one viewport camera without exposing its presentation backend.
/// </summary>
public interface IEditorViewportCoordinateConverter
{
    /// <summary>
    /// Converts viewport-local pixel coordinates to world coordinates.
    /// </summary>
    /// <param name="screenPosition">
    /// Viewport-local pixel coordinates.
    /// </param>
    /// <returns>
    /// World coordinates.
    /// </returns>
    Vector2 ScreenToWorld(Vector2 screenPosition);

    /// <summary>
    /// Converts world coordinates to viewport-local pixel coordinates.
    /// </summary>
    /// <param name="worldPosition">
    /// World coordinates.
    /// </param>
    /// <returns>
    /// Viewport-local pixel coordinates.
    /// </returns>
    Vector2 WorldToScreen(Vector2 worldPosition);
}

/// <summary>
/// Defines one presentation-independent, reloadable viewport editing tool.
/// </summary>
public abstract class EditorViewportTool
{
    /// <summary>
    /// Gets the globally stable tool identity.
    /// </summary>
    public abstract string id { get; }

    /// <summary>
    /// Gets the cursor requested while this tool is active.
    /// </summary>
    public virtual EditorViewportCursor cursor => EditorViewportCursor.Arrow;

    /// <summary>
    /// Handles a pointer press.
    /// </summary>
    /// <param name="context">
    /// Gesture and coordinate context.
    /// </param>
    /// <param name="pointer">
    /// Immutable pointer sample.
    /// </param>
    public virtual void OnPointerDown(EditorViewportToolContext context, EditorViewportPointerEvent pointer)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    /// <summary>
    /// Handles pointer movement or hover.
    /// </summary>
    /// <param name="context">
    /// Gesture and coordinate context.
    /// </param>
    /// <param name="pointer">
    /// Immutable pointer sample.
    /// </param>
    public virtual void OnPointerMove(EditorViewportToolContext context, EditorViewportPointerEvent pointer)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    /// <summary>
    /// Handles a pointer release.
    /// </summary>
    /// <param name="context">
    /// Gesture and coordinate context.
    /// </param>
    /// <param name="pointer">
    /// Immutable pointer sample.
    /// </param>
    public virtual void OnPointerUp(EditorViewportToolContext context, EditorViewportPointerEvent pointer)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    /// <summary>
    /// Handles platform cancellation of an active gesture.
    /// </summary>
    /// <param name="context">
    /// Gesture and coordinate context.
    /// </param>
    /// <param name="pointer">
    /// Immutable pointer sample.
    /// </param>
    public virtual void OnPointerCancel(EditorViewportToolContext context, EditorViewportPointerEvent pointer)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CompleteHistoryGesture(commit: false);
        context.ReleasePointer();
    }

    /// <summary>
    /// Handles a focused viewport shortcut before global action routing.
    /// </summary>
    /// <param name="context">
    /// Gesture and coordinate context.
    /// </param>
    /// <param name="shortcut">
    /// Pressed key sample.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the tool consumed the key.
    /// </returns>
    public virtual bool OnShortcut(EditorViewportToolContext context, EditorViewportShortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(context);
        return false;
    }

    /// <summary>
    /// Draws transient gizmos and diagnostics over the viewport.
    /// </summary>
    /// <param name="context">
    /// Gesture and coordinate context.
    /// </param>
    public virtual void DrawOverlay(EditorViewportToolContext context)
        => ArgumentNullException.ThrowIfNull(context);
}
