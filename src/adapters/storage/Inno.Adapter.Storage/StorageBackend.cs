namespace Inno.Adapter.Storage;

/// <summary>
/// Selects a built-in application-storage backend without exposing its implementation types.
/// </summary>
public enum StorageBackend
{
    /// <summary>
    /// Uses an operating-system file-system implementation.
    /// </summary>
    FileSystem = 0
}
