namespace Inno.Editor.Assets.AssetEditors;

/// <summary>Describes whether an editor asset operation may proceed.</summary>
public readonly record struct AssetOperationValidation
{
    /// <summary>Creates an asset operation validation result.</summary>
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

    /// <summary>Creates a failed validation result.</summary>
    public static AssetOperationValidation Invalid(string message)
        => new(false, message ?? string.Empty);
}
