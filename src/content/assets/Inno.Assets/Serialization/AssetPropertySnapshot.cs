using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Assets;

/// <summary>Contains native properties and automatically captured asset dependencies without retaining their owner or typed object.</summary>
public sealed class AssetPropertySnapshot
{
    private readonly byte[] m_data;
    /// <summary>Freezes a complete owner-produced property value.</summary>
    /// <param name="stableTypeId">Persistent identity of the exact serialized settings type.</param>
    /// <param name="data">Native property payload, copied before returning.</param>
    /// <param name="dependencies">Direct dependencies collected during the same serialization operation.</param>
    public AssetPropertySnapshot(Guid stableTypeId, ReadOnlySpan<byte> data, IEnumerable<AssetDependency> dependencies)
    {
        if (stableTypeId == Guid.Empty) throw new ArgumentException("A property snapshot requires a stable type identity.", nameof(stableTypeId));
        ArgumentNullException.ThrowIfNull(dependencies);
        this.stableTypeId = stableTypeId;
        m_data = data.ToArray();
        this.dependencies = Array.AsReadOnly(dependencies.ToArray());
    }
    /// <summary>Gets the persistent settings type identity.</summary>
    public Guid stableTypeId { get; }
    /// <summary>Gets the frozen native property bytes.</summary>
    public ReadOnlyMemory<byte> data => m_data;
    /// <summary>Gets the frozen direct dependency declarations.</summary>
    public IReadOnlyList<AssetDependency> dependencies { get; }
}
