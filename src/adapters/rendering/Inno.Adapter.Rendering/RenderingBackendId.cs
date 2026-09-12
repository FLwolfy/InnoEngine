using System;

namespace Inno.Adapter.Rendering;

/// <summary>
/// Identifies a rendering implementation independently from its graphics API and compiler language.
/// </summary>
public readonly record struct RenderingBackendId
{
    /// <summary>Creates an ordinal, case-sensitive backend identifier.</summary>
    /// <param name="value">Stable nonempty implementation identifier.</param>
    /// <exception cref="ArgumentException">The identifier is empty or contains whitespace.</exception>
    public RenderingBackendId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (char character in value)
            if (char.IsWhiteSpace(character))
                throw new ArgumentException("A rendering backend identifier cannot contain whitespace.", nameof(value));
        this.value = value;
    }

    /// <summary>Gets the identifier of the bundled BGFX implementation, not a whitelist of supported backends.</summary>
    public static RenderingBackendId bgfx { get; } = new("inno.rendering.bgfx");

    /// <summary>Gets the stable implementation identifier; the default value is unassigned.</summary>
    public string value { get; } = string.Empty;

    /// <summary>Gets whether this value identifies an implementation.</summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>Returns the identifier without resolving or creating a device.</summary>
    /// <returns>The stable identifier, or an empty string when unassigned.</returns>
    public override string ToString() => value ?? string.Empty;
}
