using System;

namespace Inno.UI;

/// <summary>
/// Identifies a document source language independently from any UI backend implementation.
/// </summary>
public readonly record struct UiDocumentLanguageId
{
    /// <summary>
    /// Creates a stable, ordinal language identifier.
    /// </summary>
    /// <param name="value">
    /// Provider-qualified nonempty identifier without whitespace.
    /// </param>
    public UiDocumentLanguageId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (char character in value)
            if (char.IsWhiteSpace(character))
                throw new ArgumentException("A UI document language identifier cannot contain whitespace.", nameof(value));
        this.value = value;
    }

    /// <summary>
    /// Gets the stable language identifier.
    /// </summary>
    public string value { get; } = string.Empty;

    /// <summary>
    /// Gets whether the identifier is assigned.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Returns the stable identifier.
    /// </summary>
    /// <returns>
    /// The identifier, or an empty string for an unassigned value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Identifies one backend-owned immutable mesh generation.
/// </summary>
/// <param name="value">
/// The concrete value read or transformed by this operation.
/// </param>
public readonly record struct UiMeshHandle(ulong value)
{
    /// <summary>
    /// Gets whether the handle identifies a mesh.
    /// </summary>
    public bool isValid => value != 0;
}

/// <summary>
/// Identifies one backend-owned immutable texture generation.
/// </summary>
/// <param name="value">
/// The concrete value read or transformed by this operation.
/// </param>
public readonly record struct UiTextureHandle(ulong value)
{
    /// <summary>
    /// Gets whether the handle identifies a texture; an unassigned handle selects the renderer's white texture.
    /// </summary>
    public bool isValid => value != 0;
}
