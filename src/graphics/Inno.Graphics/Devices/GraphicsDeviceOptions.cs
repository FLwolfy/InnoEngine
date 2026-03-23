namespace Inno.Graphics;

/// <summary>
/// Configures graphics device creation behavior.
/// </summary>
public sealed class GraphicsDeviceOptions
{
    public GraphicsBackendKind preferredBackend { get; init; } = GraphicsBackendKind.Bgfx;

    public bool enableValidation { get; init; }

    public bool enableDebugLabels { get; init; }
}
