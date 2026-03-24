
namespace Inno.Rendering;

/// <summary>
/// Represents a single render invocation request.
/// </summary>
public sealed class RenderRequest
{
    public required RenderScene scene { get; init; }

    public required RenderView view { get; init; }

    public required RenderTarget target { get; init; }
}
