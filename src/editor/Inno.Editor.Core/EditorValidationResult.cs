namespace Inno.Editor.Core;

/// <summary>Describes whether a requested editor operation is valid.</summary>
public readonly record struct EditorValidationResult(bool isValid, string message)
{
    /// <summary>Gets a successful validation result.</summary>
    public static EditorValidationResult valid => new(true, string.Empty);

    /// <summary>Creates a failed validation result.</summary>
    public static EditorValidationResult Invalid(string message)
        => new(false, message ?? string.Empty);
}
