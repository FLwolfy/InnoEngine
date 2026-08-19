namespace Inno.Assets.Core;

/// <summary>Identifies the current source and import state of an asset.</summary>
public enum AssetImportStatus
{
    /// <summary>No importer currently accepts the source.</summary>
    Unsupported,

    /// <summary>The source is waiting for import or commit.</summary>
    Pending,

    /// <summary>A valid artifact is committed for the source.</summary>
    Imported,

    /// <summary>The latest import failed and diagnostics are available.</summary>
    Failed,

    /// <summary>The source is unavailable while its persistent identity is retained.</summary>
    Missing,

    /// <summary>The source cannot be reconciled without resolving an identity conflict.</summary>
    Conflict
}
