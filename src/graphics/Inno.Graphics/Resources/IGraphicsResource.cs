namespace Inno.Graphics;

/// <summary>
/// Base contract for lifetime-managed graphics resources.
/// </summary>
public interface IGraphicsResource : IDisposable
{
    string? debugName { get; set; }
}
