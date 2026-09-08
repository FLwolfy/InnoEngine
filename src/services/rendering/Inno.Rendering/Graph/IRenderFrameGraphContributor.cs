namespace Inno.Rendering;

/// <summary>
/// Adds transient frame-final work, such as UI composition, without owning or advancing the graphics frame.
/// </summary>
public interface IRenderFrameGraphContributor
{
    /// <summary>
    /// Applies queued resource changes at the current frame safety point.
    /// </summary>
    /// <param name="frameIndex">
    /// Monotonic engine render frame index.
    /// </param>
    void PrepareFrame(ulong frameIndex);

    /// <summary>
    /// Adds frame-scoped passes after all user render-request graphs have executed.
    /// </summary>
    /// <param name="graph">
    /// Shared final graph builder.
    /// </param>
    /// <param name="frameIndex">
    /// Monotonic engine render frame index.
    /// </param>
    void AddRenderPasses(RenderGraphBuilder graph, ulong frameIndex);
}
