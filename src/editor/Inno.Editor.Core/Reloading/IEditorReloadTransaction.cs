namespace Inno.Editor.Core;

/// <summary>
/// Coordinates one editor feature's live-state migration around an atomic assembly generation
/// switch.
/// </summary>
public interface IEditorReloadTransaction
{
    /// <summary>
    /// Quiesces retiring objects before the candidate assembly generation becomes active.
    /// </summary>
    void PrepareForActivation();

    /// <summary>
    /// Applies captured state to objects resolved from the active candidate generation.
    /// </summary>
    void Apply();

    /// <summary>
    /// Finalizes a successful migration and releases retiring feature objects.
    /// </summary>
    /// <remarks>
    /// Implementations must isolate cleanup failures because assembly publication is already final
    /// when this method runs.
    /// </remarks>
    void Complete();

    /// <summary>
    /// Restores the previous feature structure before the previous assembly generation is restored.
    /// </summary>
    void RollbackStructure();

    /// <summary>
    /// Restores captured values and lifecycle state after the previous assembly generation is active.
    /// </summary>
    void RestorePreviousState();
}
