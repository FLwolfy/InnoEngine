using System.Collections.Generic;
using Inno.References;

namespace Inno.Scene;

/// <summary>
/// Represents a staged scene object state transfer associated with one assembly reload transaction.
/// </summary>
public interface ISceneReloadStateTransfer
{
    /// <summary>
    /// Gets old scene objects whose runtime types belong to the retiring assembly generation.
    /// </summary>
    IReadOnlyList<object> retiredObjects { get; }

    /// <summary>
    /// Gets non-fatal state transfer decisions produced while replacing reloadable scene objects.
    /// </summary>
    IReadOnlyList<SceneReloadDiagnostic> diagnostics { get; }

    /// <summary>
    /// Gets the shared candidate resolutions for actual scene element and asset dependency slots after application.
    /// </summary>
    IReadOnlyList<ReferenceRecoveryChange> recoveryChanges { get; }

    /// <summary>
    /// Disables active retiring lifecycle objects before the new assembly generation becomes active.
    /// </summary>
    void PrepareForActivation();

    /// <summary>
    /// Creates replacement instances and restores their serialized state.
    /// </summary>
    void Apply();

    /// <summary>
    /// Restores the previous scene structure after a failed state transfer.
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
