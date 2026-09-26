using System;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Scripting.Api;

namespace Inno.Editor.Assets;

/// <summary>
/// Declares the source name, extension, and menu placement that cannot be inferred from an
/// <see cref="AssetObject"/> type alone.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AssetCreationMenuAttribute : Attribute
{
    /// <summary>
    /// Creates one asset creation menu declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable template identifier.
    /// </param>
    /// <param name="menuPath">
    /// Slash-delimited path below the File Browser Create menu.
    /// </param>
    /// <param name="extension">
    /// Native source extension, including its leading dot.
    /// </param>
    /// <param name="defaultName">
    /// Default source name without an extension or numeric suffix.
    /// </param>
    /// <param name="groupOrder">
    /// Stable order of the top-level category below Create.
    /// </param>
    /// <param name="itemOrder">
    /// Stable order of the leaf within its immediate category.
    /// </param>
    /// <param name="separatorBeforeGroup">
    /// Whether the top-level category starts after a separator.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when an identifier, path, source name, or native extension is invalid.
    /// </exception>
    public AssetCreationMenuAttribute(
        string id,
        string menuPath,
        string extension,
        string defaultName,
        int groupOrder = 0,
        int itemOrder = 0,
        bool separatorBeforeGroup = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(menuPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultName);
        if (extension.Length == 0 || extension[0] != '.'
            || extension.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException(
                "A native source extension beginning with a dot is required.",
                nameof(extension));
        }

        string normalizedPath = menuPath.Replace('\\', '/').Trim('/');
        if (normalizedPath.Length == 0
            || Array.Exists(normalizedPath.Split('/'), static segment => string.IsNullOrWhiteSpace(segment)))
        {
            throw new ArgumentException("A valid menu path is required.", nameof(menuPath));
        }
        if (defaultName.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("The default name cannot contain a path separator.", nameof(defaultName));

        this.id = id;
        this.menuPath = normalizedPath;
        this.extension = extension.ToLowerInvariant();
        this.defaultName = defaultName.Trim();
        this.groupOrder = groupOrder;
        this.itemOrder = itemOrder;
        this.separatorBeforeGroup = separatorBeforeGroup;
    }

    /// <summary>
    /// Gets the globally stable template identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the slash-delimited path below the File Browser Create menu.
    /// </summary>
    public string menuPath { get; }

    /// <summary>
    /// Gets the native source extension, including its leading dot.
    /// </summary>
    public string extension { get; }

    /// <summary>
    /// Gets the default source name without an extension or numeric suffix.
    /// </summary>
    public string defaultName { get; }

    /// <summary>
    /// Gets the stable order of the top-level category below Create.
    /// </summary>
    public int groupOrder { get; }

    /// <summary>
    /// Gets the stable order of the leaf within its immediate category.
    /// </summary>
    public int itemOrder { get; }

    /// <summary>
    /// Gets whether the top-level category starts after a separator.
    /// </summary>
    public bool separatorBeforeGroup { get; }
}

/// <summary>
/// Produces the detached initial value for one authorable native asset source.
/// </summary>
/// <remarks>
/// Derivation is the discovery contract. <see cref="AssetCreationMenuAttribute"/> supplies only
/// source-format and presentation data that the asset inheritance hierarchy cannot express.
/// </remarks>
public abstract class AssetCreationTemplate
{
    /// <summary>
    /// Gets the concrete asset type produced by this template.
    /// </summary>
    public abstract Type assetType { get; }

    /// <summary>
    /// Creates a detached default value that has not been imported or published.
    /// </summary>
    /// <returns>
    /// A new asset value owned by the caller.
    /// </returns>
    public abstract AssetObject Create();

    /// <summary>
    /// Encodes the detached initial value into the source representation owned by this template.
    /// </summary>
    /// <param name="context">
    /// Creation services scoped to the current Asset source pipeline.
    /// </param>
    /// <param name="asset">
    /// The exact value returned by <see cref="Create"/>.
    /// </param>
    /// <returns>
    /// Complete source bytes ready for an atomic File Browser create operation.
    /// </returns>
    /// <remarks>
    /// The default implementation uses native structured Asset serialization. Templates paired
    /// with a textual or custom binary importer can override this method without changing the
    /// File Browser or registering a second creation protocol.
    /// </remarks>
    public virtual byte[] Encode(AssetCreationContext context, AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(asset);
        return context.EncodeNative(asset);
    }
}

/// <summary>
/// Provides source encoding services to one detached Asset creation template invocation.
/// </summary>
public sealed class AssetCreationContext
{
    private readonly AssetSourceStore m_sources;

    /// <summary>
    /// Creates a context over the source serializer owned by the active Asset pipeline.
    /// </summary>
    /// <param name="sources">
    /// Source serializer carrying the active reference context.
    /// </param>
    [ScriptingApiIgnore]
    public AssetCreationContext(AssetSourceStore sources)
        => m_sources = sources ?? throw new ArgumentNullException(nameof(sources));

    /// <summary>
    /// Encodes an Asset value through the current native structured source serializer.
    /// </summary>
    /// <param name="asset">
    /// Detached Asset value to encode.
    /// </param>
    /// <returns>
    /// Complete native source bytes, including the concrete serialized type identity.
    /// </returns>
    public byte[] EncodeNative(AssetObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return m_sources.Encode(asset);
    }
}

/// <summary>
/// Creates a default-constructed native source for an authorable asset type.
/// </summary>
/// <typeparam name="TAsset">
/// Concrete serializable asset type.
/// </typeparam>
public abstract class AssetCreationTemplate<TAsset> : AssetCreationTemplate
    where TAsset : AssetObject, new()
{
    /// <summary>
    /// Gets the asset type created by this Editor template.
    /// </summary>
    public sealed override Type assetType => typeof(TAsset);

    /// <summary>
    /// Creates a value using this implementation's validated inputs.
    /// </summary>
    /// <returns>
    /// The validated asset object that represents the completed operation.
    /// </returns>
    public override AssetObject Create() => CreateAsset();

    /// <summary>
    /// Creates the initial detached value, allowing specialized deterministic defaults.
    /// </summary>
    /// <returns>
    /// A new asset value owned by the caller.
    /// </returns>
    protected virtual TAsset CreateAsset() => new();
}
