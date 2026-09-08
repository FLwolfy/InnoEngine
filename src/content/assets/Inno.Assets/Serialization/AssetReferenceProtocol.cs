using Inno.References;

namespace Inno.Assets;

/// <summary>
/// Defines the open cross-domain protocol used to resolve persistent asset identities.
/// </summary>
public static class AssetReferenceProtocol
{
    /// <summary>
    /// Gets the stable asset reference kind registered in shared reference catalogs.
    /// </summary>
    public static ReferenceKindId id { get; } = new("inno.reference.asset");
}
