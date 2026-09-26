using System;
using System.Collections.Generic;
using Inno.Core.Input;
using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Frame-local input located in the output viewport's physical pixels.
/// </summary>
public sealed class RenderOutputInput
{
    /// <summary>
    /// Creates an input snapshot for one output viewport.
    /// </summary>
    /// <param name="pointerPosition">
    /// Pointer coordinates relative to the viewport's top-left corner.
    /// </param>
    /// <param name="pointerInside">
    /// Whether the pointer is within this output.
    /// </param>
    /// <param name="scrollDelta">
    /// Pointer wheel movement.
    /// </param>
    /// <param name="modifiers">
    /// Current keyboard modifiers.
    /// </param>
    /// <param name="keysPressed">
    /// Keys pressed this frame.
    /// </param>
    /// <param name="keysReleased">
    /// Keys released this frame.
    /// </param>
    /// <param name="buttonsPressed">
    /// Pointer buttons pressed this frame.
    /// </param>
    /// <param name="buttonsReleased">
    /// Pointer buttons released this frame.
    /// </param>
    /// <param name="textInput">
    /// Ordered Unicode text commits.
    /// </param>
    /// <param name="interactionEnabled">
    /// Whether the output may dispatch game interaction.
    /// </param>
    public RenderOutputInput(Vector2 pointerPosition, bool pointerInside, Vector2 scrollDelta,
        KeyModifier modifiers, IReadOnlyCollection<KeyCode> keysPressed,
        IReadOnlyCollection<KeyCode> keysReleased,
        IReadOnlyCollection<MouseButton> buttonsPressed,
        IReadOnlyCollection<MouseButton> buttonsReleased,
        IReadOnlyList<string> textInput,
        bool interactionEnabled = true)
    {
        this.pointerPosition = pointerPosition;
        this.pointerInside = pointerInside;
        this.scrollDelta = scrollDelta;
        this.modifiers = modifiers;
        this.keysPressed = keysPressed ?? throw new ArgumentNullException(nameof(keysPressed));
        this.keysReleased = keysReleased ?? throw new ArgumentNullException(nameof(keysReleased));
        this.buttonsPressed = buttonsPressed ?? throw new ArgumentNullException(nameof(buttonsPressed));
        this.buttonsReleased = buttonsReleased ?? throw new ArgumentNullException(nameof(buttonsReleased));
        this.textInput = textInput ?? throw new ArgumentNullException(nameof(textInput));
        this.interactionEnabled = interactionEnabled;
    }

    /// <summary>
    /// Gets a snapshot with no active input.
    /// </summary>
    public static RenderOutputInput empty { get; } = new(default, false, default,
        KeyModifier.None, [], [], [], [], []);

    /// <summary>
    /// Gets a snapshot that clears gameplay interaction while retaining the rendered output.
    /// </summary>
    public static RenderOutputInput suspended { get; } = new(default, false, default,
        KeyModifier.None, [], [], [], [], [], interactionEnabled: false);

    /// <summary>
    /// Gets viewport-local pointer coordinates.
    /// </summary>
    public Vector2 pointerPosition { get; }
    /// <summary>
    /// Gets whether the pointer is inside this output.
    /// </summary>
    public bool pointerInside { get; }
    /// <summary>
    /// Gets wheel movement.
    /// </summary>
    public Vector2 scrollDelta { get; }
    /// <summary>
    /// Gets active keyboard modifiers.
    /// </summary>
    public KeyModifier modifiers { get; }
    /// <summary>
    /// Gets keys pressed this frame.
    /// </summary>
    public IReadOnlyCollection<KeyCode> keysPressed { get; }
    /// <summary>
    /// Gets keys released this frame.
    /// </summary>
    public IReadOnlyCollection<KeyCode> keysReleased { get; }
    /// <summary>
    /// Gets buttons pressed this frame.
    /// </summary>
    public IReadOnlyCollection<MouseButton> buttonsPressed { get; }
    /// <summary>
    /// Gets buttons released this frame.
    /// </summary>
    public IReadOnlyCollection<MouseButton> buttonsReleased { get; }
    /// <summary>
    /// Gets ordered text commits.
    /// </summary>
    public IReadOnlyList<string> textInput { get; }

    /// <summary>
    /// Gets whether game pointer and keyboard interaction is enabled.
    /// </summary>
    public bool interactionEnabled { get; }
}
