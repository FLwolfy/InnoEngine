using System;

using Inno.Extensibility.Modules;

namespace Inno.Scripting.Reload;

/// <summary>
/// Coordinates host-owned state with an atomic script assembly generation transition.
/// </summary>
public interface IScriptReloadCoordinator
{
    /// <summary>
    /// Commits a prepared assembly reload together with dependent host state.
    /// </summary>
    /// <param name="reload">
    /// The prepared assembly reload session to activate and complete.
    /// </param>
    /// <param name="activateExternalCandidate">
    /// Optional action that provisionally activates related candidate state.
    /// </param>
    /// <param name="restoreExternalState">
    /// Optional action that restores related active state after rollback.
    /// </param>
    /// <returns>
    /// A monitor observing cooperative unload of assemblies retired by the committed generation.
    /// </returns>
    AssemblyUnloadMonitor Execute(
        AssemblyReloadSession reload,
        Action? activateExternalCandidate = null,
        Action? restoreExternalState = null);

    /// <summary>
    /// Requests diagnostics derived from the active host generation to be republished.
    /// </summary>
    void RefreshDiagnostics();
}
