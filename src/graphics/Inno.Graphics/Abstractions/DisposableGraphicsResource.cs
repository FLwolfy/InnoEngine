
namespace Inno.Graphics;

/// <summary>
/// Provides a minimal disposable resource base.
/// </summary>
public abstract class DisposableGraphicsResource : GraphicsObjectBase, IGraphicsResource
{
    private bool m_disposed;

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        Dispose(true);
        m_disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resource-specific state.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}
