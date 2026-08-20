namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Describes whether an editor asset operation may proceed.</summary>
public readonly record struct AssetOperationValidation
{
    /// <summary>
    /// Creates the validation result returned before an asset transaction begins.
    /// </summary>
    /// <param name="isValid">Whether the requested asset operation may proceed.</param>
    /// <param name="message">The user-facing rejection diagnostic, or an empty string for a valid result.</param>
    public AssetOperationValidation(bool isValid, string message)
    {
        this.isValid = isValid;
        this.message = message ?? string.Empty;
    }

    /// <summary>Gets whether the operation may proceed.</summary>
    public bool isValid { get; }

    /// <summary>Gets the validation diagnostic.</summary>
    public string message { get; }

    /// <summary>Gets a successful validation result.</summary>
    public static AssetOperationValidation valid => new(true, string.Empty);

    /// <summary>
    /// Creates a failed asset-operation validation result.
    /// </summary>
    /// <param name="message">The user-facing diagnostic explaining why the operation was rejected.</param>
    /// <returns>An invalid validation result containing the supplied diagnostic.</returns>
    public static AssetOperationValidation Invalid(string message)
        => new(false, message ?? string.Empty);
}
