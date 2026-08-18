using System.Collections.Generic;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Represents a staged scene object migration associated with one assembly reload transaction.
/// </summary>
public interface ISceneReloadMigration
{
    /// <summary>
    /// Gets old scene objects whose runtime types belong to the retiring assembly generation.
    /// </summary>
    IReadOnlyList<object> retiredObjects { get; }

    /// <summary>
    /// Disables active retiring lifecycle objects before the new assembly generation becomes active.
    /// </summary>
    void PrepareForActivation();

    /// <summary>
    /// Creates replacement instances and restores their serialized state.
    /// </summary>
    void Apply();

    /// <summary>
    /// Restores the previous scene structure after a failed migration.
    /// </summary>
    void RollbackStructure();

    /// <summary>
    /// Restores lifecycle state on the previous scene instances after rollback.
    /// </summary>
    void RestorePreviousState();

    /// <summary>
    /// Finalizes replacement instances after the assembly reload is committed.
    /// </summary>
    void Complete();
}
