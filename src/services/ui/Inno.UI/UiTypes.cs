using System;
using System.Collections.Generic;

using Inno.Core.Input;
using Inno.Core.Mathematics;

namespace Inno.UI;

/// <summary>
/// Defines one independent UI layout and interaction surface.
/// </summary>
public readonly record struct UiContextOptions
{
    /// <summary>
    /// Creates validated context options.
    /// </summary>
    /// <param name="name">
    /// A non-empty diagnostic context name.
    /// </param>
    /// <param name="width">
    /// The positive initial width in pixels.
    /// </param>
    /// <param name="height">
    /// The positive initial height in pixels.
    /// </param>
    /// <param name="density">
    /// The positive density-independent pixel ratio.
    /// </param>
    public UiContextOptions(string name, int width, int height, float density = 1f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(density) || density <= 0f)
            throw new ArgumentOutOfRangeException(nameof(density));
        this.name = name.Trim();
        this.width = width;
        this.height = height;
        this.density = density;
    }

    /// <summary>
    /// Gets the diagnostic context name.
    /// </summary>
    public string name { get; }
    /// <summary>
    /// Gets the initial width in pixels.
    /// </summary>
    public int width { get; }
    /// <summary>
    /// Gets the initial height in pixels.
    /// </summary>
    public int height { get; }
    /// <summary>
    /// Gets the density-independent pixel ratio.
    /// </summary>
    public float density { get; }
}

/// <summary>
/// Contains immutable text in one explicitly selected UI document language.
/// </summary>
public sealed class UiDocumentSource
{
    /// <summary>
    /// Creates a validated in-memory document source.
    /// </summary>
    /// <param name="language">
    /// Explicit source language understood by a registered backend.
    /// </param>
    /// <param name="text">
    /// Complete immutable document text.
    /// </param>
    /// <param name="sourceUri">
    /// Virtual source address used only for diagnostics and relative dependency identity.
    /// </param>
    public UiDocumentSource(UiDocumentLanguageId language, string text, string sourceUri = "memory://ui-document")
    {
        if (!language.isValid) throw new ArgumentException("A document source requires an assigned language.", nameof(language));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUri);
        this.language = language;
        this.text = text;
        this.sourceUri = sourceUri;
    }

    /// <summary>
    /// Gets the explicitly selected source language.
    /// </summary>
    public UiDocumentLanguageId language { get; }
    /// <summary>
    /// Gets the complete immutable source text.
    /// </summary>
    public string text { get; }
    /// <summary>
    /// Gets the virtual source address.
    /// </summary>
    public string sourceUri { get; }
}

/// <summary>
/// Contains a language-tagged fragment for a backend-specific document mutation.
/// </summary>
public readonly record struct UiDocumentFragment
{
    /// <summary>
    /// Creates a validated source fragment.
    /// </summary>
    /// <param name="language">
    /// Explicit fragment language.
    /// </param>
    /// <param name="text">
    /// Complete fragment text.
    /// </param>
    public UiDocumentFragment(UiDocumentLanguageId language, string text)
    {
        if (!language.isValid) throw new ArgumentException("A document fragment requires an assigned language.", nameof(language));
        ArgumentNullException.ThrowIfNull(text);
        this.language = language;
        this.text = text;
    }

    /// <summary>
    /// Gets the explicitly selected fragment language.
    /// </summary>
    public UiDocumentLanguageId language { get; }
    /// <summary>
    /// Gets the complete fragment text.
    /// </summary>
    public string text { get; }
}

/// <summary>
/// Contains immutable RGBA8 texture pixels addressable by a document source name.
/// </summary>
public sealed class UiTextureData
{
    private readonly byte[] m_pixels;

    /// <summary>
    /// Creates a validated immutable texture.
    /// </summary>
    /// <param name="width">
    /// The positive texture width.
    /// </param>
    /// <param name="height">
    /// The positive texture height.
    /// </param>
    /// <param name="pixels">
    /// Tightly packed RGBA8 pixels.
    /// </param>
    public UiTextureData(int width, int height, ReadOnlySpan<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("UI texture pixels must be tightly packed RGBA8.", nameof(pixels));
        this.width = width;
        this.height = height;
        m_pixels = pixels.ToArray();
    }

    /// <summary>
    /// Gets the texture width.
    /// </summary>
    public int width { get; }
    /// <summary>
    /// Gets the texture height.
    /// </summary>
    public int height { get; }
    /// <summary>
    /// Gets the immutable RGBA8 pixels.
    /// </summary>
    public ReadOnlyMemory<byte> pixels => m_pixels;
}

/// <summary>
/// Identifies one document event made visible to game code.
/// </summary>
public enum UiEventType
{
    /// <summary>
    /// The target was clicked.
    /// </summary>
    Click,
    /// <summary>
    /// The target value changed.
    /// </summary>
    Change,
    /// <summary>
    /// A form was submitted.
    /// </summary>
    Submit,
    /// <summary>
    /// The target gained focus.
    /// </summary>
    Focus,
    /// <summary>
    /// The target lost focus.
    /// </summary>
    Blur,
    /// <summary>
    /// A pointer entered the target.
    /// </summary>
    MouseEnter,
    /// <summary>
    /// A pointer left the target.
    /// </summary>
    MouseLeave
}

/// <summary>
/// Describes one queued document event after a context update.
/// </summary>
/// <param name="type">
/// The ui event type value used to initialize this instance.
/// </param>
/// <param name="document">
/// The ui document handle value used to initialize this instance.
/// </param>
/// <param name="targetId">
/// The string value used to initialize this instance.
/// </param>
public readonly record struct UiEvent(
    UiEventType type,
    UiDocumentHandle document,
    string targetId);

/// <summary>
/// Carries one immutable input snapshot across the UI backend boundary.
/// </summary>
public sealed class UiInputSnapshot
{
    /// <summary>
    /// Creates one backend-neutral UI input snapshot.
    /// </summary>
    /// <param name="mousePosition">
    /// Pointer position in surface coordinates.
    /// </param>
    /// <param name="scrollDelta">
    /// Pointer wheel movement.
    /// </param>
    /// <param name="modifiers">
    /// Active keyboard modifiers.
    /// </param>
    /// <param name="keysPressed">
    /// Physical keys pressed this frame.
    /// </param>
    /// <param name="keysReleased">
    /// Physical keys released this frame.
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
    public UiInputSnapshot(
        Vector2 mousePosition,
        Vector2 scrollDelta,
        KeyModifier modifiers,
        IReadOnlyCollection<KeyCode> keysPressed,
        IReadOnlyCollection<KeyCode> keysReleased,
        IReadOnlyCollection<MouseButton> buttonsPressed,
        IReadOnlyCollection<MouseButton> buttonsReleased,
        IReadOnlyList<string> textInput)
    {
        this.mousePosition = mousePosition;
        this.scrollDelta = scrollDelta;
        this.modifiers = modifiers;
        this.keysPressed = keysPressed ?? throw new ArgumentNullException(nameof(keysPressed));
        this.keysReleased = keysReleased ?? throw new ArgumentNullException(nameof(keysReleased));
        this.buttonsPressed = buttonsPressed ?? throw new ArgumentNullException(nameof(buttonsPressed));
        this.buttonsReleased = buttonsReleased ?? throw new ArgumentNullException(nameof(buttonsReleased));
        this.textInput = textInput ?? throw new ArgumentNullException(nameof(textInput));
    }

    /// <summary>
    /// Gets the pointer position.
    /// </summary>
    public Vector2 mousePosition { get; }
    /// <summary>
    /// Gets pointer wheel movement.
    /// </summary>
    public Vector2 scrollDelta { get; }
    /// <summary>
    /// Gets active keyboard modifiers.
    /// </summary>
    public KeyModifier modifiers { get; }
    /// <summary>
    /// Gets physical keys pressed this frame.
    /// </summary>
    public IReadOnlyCollection<KeyCode> keysPressed { get; }
    /// <summary>
    /// Gets physical keys released this frame.
    /// </summary>
    public IReadOnlyCollection<KeyCode> keysReleased { get; }
    /// <summary>
    /// Gets pointer buttons pressed this frame.
    /// </summary>
    public IReadOnlyCollection<MouseButton> buttonsPressed { get; }
    /// <summary>
    /// Gets pointer buttons released this frame.
    /// </summary>
    public IReadOnlyCollection<MouseButton> buttonsReleased { get; }
    /// <summary>
    /// Gets ordered Unicode text commits.
    /// </summary>
    public IReadOnlyList<string> textInput { get; }
}
