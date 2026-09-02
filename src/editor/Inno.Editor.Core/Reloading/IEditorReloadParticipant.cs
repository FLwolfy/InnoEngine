using Inno.Extensibility.Modules;

namespace Inno.Editor.Core;

/// <summary>
/// Contributes editor-owned live state to an assembly reload without coupling the reload requester
/// to a feature domain.
/// </summary>
public interface IEditorReloadParticipant
{
    /// <summary>
    /// Captures one isolated transaction for the prepared assembly generation.
    /// </summary>
    /// <param name="context">
    /// The prepared assembly reload context containing previous and candidate participant state.
    /// </param>
    /// <returns>
    /// A transaction that has captured current state but has not modified live editor objects.
    /// </returns>
    IEditorReloadTransaction Capture(AssemblyReloadContext context);

    /// <summary>
    /// Republishes diagnostics derived from the participant's current live state.
    /// </summary>
    void RefreshDiagnostics();
}
