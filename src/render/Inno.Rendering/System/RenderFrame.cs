
namespace Inno.Rendering;

/// <summary>
/// Represents per-frame render state.
/// </summary>
public sealed class RenderFrame
{
    public required ulong frameIndex { get; init; }

    public required DateTimeOffset timestamp { get; init; }

    public RenderFrameStatistics statistics { get; } = new();
}
