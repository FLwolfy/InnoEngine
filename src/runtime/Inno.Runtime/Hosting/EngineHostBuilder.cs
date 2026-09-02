using System;
using System.IO;

namespace Inno.Runtime;

/// <summary>
/// Collects application-level runtime services before creating an <see cref="EngineHost"/>.
/// </summary>
public sealed class EngineHostBuilder
{
    private string m_metadataCacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoEngine",
        "RuntimeMetadata");

    /// <summary>
    /// Selects the writable cache used for assembly shadow copies and immutable type metadata.
    /// </summary>
    /// <param name="directory">
    /// The absolute or current-directory-relative cache directory.
    /// </param>
    /// <returns>
    /// This builder for fluent configuration.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="directory"/> is empty.
    /// </exception>
    public EngineHostBuilder UseMetadataCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        m_metadataCacheDirectory = Path.GetFullPath(directory);
        return this;
    }

    /// <summary>
    /// Creates an application host and acquires its immutable metadata services.
    /// </summary>
    /// <returns>
    /// A host owned by the caller.
    /// </returns>
    public EngineHost Build() => new(m_metadataCacheDirectory);
}
