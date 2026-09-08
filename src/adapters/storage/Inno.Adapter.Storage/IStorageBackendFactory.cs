using Inno.Storage;

namespace Inno.Adapter.Storage;

/// <summary>
/// Creates isolated application-storage instances from explicit backend selections.
/// </summary>
public interface IStorageBackendFactory
{
    /// <summary>
    /// Creates storage rooted at the supplied host-owned directory.
    /// </summary>
    /// <param name="backend">
    /// Built-in storage backend selected by the composition root.
    /// </param>
    /// <param name="rootDirectory">
    /// Absolute or relative root assigned exclusively to the returned storage instance.
    /// </param>
    /// <returns>
    /// A caller-owned backend-neutral application storage service.
    /// </returns>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when the catalog does not contain the selected backend.
    /// </exception>
    IApplicationStorage CreateStorage(StorageBackend backend, string rootDirectory);
}
