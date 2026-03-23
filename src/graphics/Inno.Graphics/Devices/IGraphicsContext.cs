namespace Inno.Graphics;

/// <summary>
/// Represents a per-thread graphics context.
/// </summary>
public interface IGraphicsContext
{
    /// <summary>
    /// Gets the owning graphics device.
    /// </summary>
    IGraphicsDevice device { get; }
}
