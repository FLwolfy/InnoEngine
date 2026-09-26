using Inno.Text;

namespace Inno.Adapter.Text;

/// <summary>
/// Creates isolated text backends without exposing implementation assemblies to composition code.
/// </summary>
public interface ITextBackendFactory
{
    /// <summary>
    /// Creates one caller-owned text backend.
    /// </summary>
    /// <param name="backend">
    /// The selected backend implementation.
    /// </param>
    /// <returns>
    /// A newly allocated text backend.
    /// </returns>
    ITextBackend CreateBackend(TextBackend backend);
}
