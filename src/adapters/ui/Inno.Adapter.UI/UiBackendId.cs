using System;

namespace Inno.Adapter.UI;

/// <summary>
/// Identifies a UI implementation independently from its document language.
/// </summary>
public readonly record struct UiBackendId
{
    /// <summary>
    /// Creates an ordinal, case-sensitive backend identifier.
    /// </summary>
    /// <param name="value">
    /// Stable nonempty implementation identifier.
    /// </param>
    public UiBackendId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (char character in value)
            if (char.IsWhiteSpace(character))
                throw new ArgumentException("A UI backend identifier cannot contain whitespace.", nameof(value));
        this.value = value;
    }

    /// <summary>
    /// Gets the identifier of the bundled RmlUi implementation, not a closed backend list.
    /// </summary>
    public static UiBackendId rmlUi { get; } = new("inno.ui.rmlui");

    /// <summary>
    /// Gets the stable identifier; the default value is unassigned.
    /// </summary>
    public string value { get; } = string.Empty;

    /// <summary>
    /// Gets whether this value identifies an implementation.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Returns the stable identifier.
    /// </summary>
    /// <returns>
    /// The identifier, or an empty string when unassigned.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}
