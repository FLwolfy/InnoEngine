namespace Inno.Adapter.Presentation;

/// <summary>
/// Selects a built-in host presentation backend without exposing platform or renderer implementations.
/// </summary>
public enum PresentationBackend
{
    /// <summary>
    /// Uses the engine's Dear ImGui presentation implementation.
    /// </summary>
    ImGui = 0
}
