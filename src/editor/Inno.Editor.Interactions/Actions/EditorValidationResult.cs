namespace Inno.Editor.Interactions;

/// <summary>
/// Describes whether a requested editor operation is valid and carries a diagnostic when it is rejected.
/// </summary>
/// <param name="isValid">Whether the requested operation may proceed.</param>
/// <param name="message">The user-facing validation diagnostic, or an empty string for a valid result.</param>
public readonly record struct EditorValidationResult(bool isValid, string message)
{
    /// <summary>Gets a successful validation result.</summary>
    public static EditorValidationResult valid => new(true, string.Empty);

    /// <summary>
    /// Creates a failed validation result with a user-facing diagnostic.
    /// </summary>
    /// <param name="message">The diagnostic that explains why the operation cannot proceed.</param>
    /// <returns>An invalid result containing the supplied diagnostic.</returns>
    public static EditorValidationResult Invalid(string message)
        => new(false, message ?? string.Empty);
}
