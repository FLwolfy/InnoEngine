using System;
using System.Collections.Generic;
using System.Reflection;

namespace Inno.Extensibility.Modules;

/// <summary>
/// Represents an immutable view of the assemblies participating in one catalog generation.
/// </summary>
/// <remarks>
/// A snapshot retains its assemblies. Consumers must not keep obsolete snapshots after a reload,
/// because doing so can delay unloading a collectible assembly context.
/// </remarks>
public sealed class AssemblyCatalogSnapshot
{
    private readonly Assembly[] m_assemblies;

    internal AssemblyCatalogSnapshot(long version, Assembly[] assemblies)
    {
        this.version = version;
        m_assemblies = assemblies;
    }

    /// <summary>
    /// Gets the monotonically increasing catalog version.
    /// </summary>
    public long version { get; }

    /// <summary>
    /// Gets the host and active module assemblies in this generation.
    /// </summary>
    public IReadOnlyList<Assembly> assemblies => m_assemblies;
}
