namespace Inno.Assets;

/// <summary>
/// Identifies a committed asset database change.
/// </summary>
public enum AssetChangeKind
{
    /// <summary>
    /// A source was added to the database.
    /// </summary>
    Added,

    /// <summary>
    /// An existing source or artifact changed.
    /// </summary>
    Modified,

    /// <summary>
    /// A source moved while retaining its persistent identity.
    /// </summary>
    Moved,

    /// <summary>
    /// A source and its persistent metadata were removed.
    /// </summary>
    Removed,

    /// <summary>
    /// A source became unavailable while retaining its identity.
    /// </summary>
    Missing,

    /// <summary>
    /// A canonical runtime object was replaced by an incompatible imported type.
    /// </summary>
    Replaced,

    /// <summary>
    /// An import status or diagnostic changed without replacing the source.
    /// </summary>
    StatusChanged
}
