using Inno.Extensibility.Reload;

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
    /// <param name="externalChange">
    /// Optional five-phase content change; captured before publication and restored before domain property recovery.
    /// </param>
    /// <returns>
    /// A monitor observing cooperative unload of assemblies retired by the committed generation.
    /// </returns>
    AssemblyUnloadMonitor Execute(
        AssemblyReloadSession reload,
        IGenerationChange? externalChange = null);

    /// <summary>
    /// Requests diagnostics derived from the active host generation to be republished.
    /// </summary>
    void RefreshDiagnostics();
}
