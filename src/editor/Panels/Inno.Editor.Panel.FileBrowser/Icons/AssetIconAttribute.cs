using System;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>
/// Declares the icon used by the Asset Browser for an imported asset type or source extension.
/// </summary>
/// <remarks>
/// Apply one or more declarations to a container type. The container has no runtime responsibility;
/// it only keeps related icon declarations together so the type catalog can discover them.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Interface |
    AttributeTargets.Enum |
    AttributeTargets.Delegate,
    AllowMultiple = true,
    Inherited = false)]
public sealed class AssetIconAttribute : Attribute
{
    /// <summary>
    /// Creates an icon declaration using a glyph from the Editor icon catalog.
    /// </summary>
    /// <param name="assetType">
    /// The imported asset type represented by the icon.
    /// </param>
    /// <param name="icon">
    /// The Editor icon glyph to render.
    /// </param>
    /// <param name="useForChildren">
    /// Whether the declaration may also represent derived asset types.
    /// </param>
    /// <param name="priority">
    /// The tie-breaking priority after exactness and inheritance distance.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="assetType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="icon"/> is empty.
    /// </exception>
    public AssetIconAttribute(
        Type assetType,
        string icon,
        bool useForChildren = false,
        int priority = 0)
    {
        this.assetType = assetType ?? throw new ArgumentNullException(nameof(assetType));
        this.icon = ValidateIcon(icon);
        this.useForChildren = useForChildren;
        this.priority = priority;
    }

    /// <summary>
    /// Creates an icon declaration for files ending with a source extension.
    /// </summary>
    /// <param name="extension">
    /// The simple or compound file extension. A leading period is optional and matching is case-insensitive.
    /// </param>
    /// <param name="icon">
    /// The Editor icon glyph to render.
    /// </param>
    /// <param name="priority">
    /// The tie-breaking priority after extension specificity.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="extension"/> is empty or contains a path separator, or when
    /// <paramref name="icon"/> is empty.
    /// </exception>
    public AssetIconAttribute(
        string extension,
        string icon,
        int priority = 0)
    {
        this.extension = NormalizeExtension(extension);
        this.icon = ValidateIcon(icon);
        this.priority = priority;
    }

    /// <summary>
    /// Gets the imported asset type represented by this declaration, or <see langword="null"/> for
    /// an extension declaration.
    /// </summary>
    public Type? assetType { get; }

    /// <summary>
    /// Gets the normalized source extension represented by this declaration, or <see langword="null"/>
    /// for a type declaration.
    /// </summary>
    public string? extension { get; }

    /// <summary>
    /// Gets the Editor icon glyph to render.
    /// </summary>
    public string icon { get; }

    /// <summary>
    /// Gets whether a type declaration may also represent derived asset types. Extension declarations
    /// always return <see langword="false"/>.
    /// </summary>
    public bool useForChildren { get; }

    /// <summary>
    /// Gets the tie-breaking priority after target specificity.
    /// </summary>
    public int priority { get; }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("A source file extension is required.", nameof(extension));
        string normalized = extension.Trim();
        if (normalized.Contains('/') || normalized.Contains('\\'))
            throw new ArgumentException("A source file extension cannot contain path separators.", nameof(extension));
        if (!normalized.StartsWith(".", StringComparison.Ordinal))
            normalized = "." + normalized;
        if (normalized.Length == 1)
            throw new ArgumentException("A source file extension must contain a suffix.", nameof(extension));
        return normalized.ToLowerInvariant();
    }

    private static string ValidateIcon(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            throw new ArgumentException("An Editor icon glyph is required.", nameof(icon));
        return icon;
    }
}
