using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Editor.Inspection;

/// <summary>Represents an ordered multi-asset selection without retaining asset or plugin-generation objects.</summary>
public sealed class AssetInspectionSelection
{
    /// <summary>Captures unique persistent identities in selection order.</summary>
    /// <param name="assetIds">Nonempty identities; the last identity is the primary selection.</param>
    public AssetInspectionSelection(IEnumerable<Guid> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        Guid[] values = assetIds.Distinct().ToArray();
        if (values.Length == 0 || values.Contains(Guid.Empty)) throw new ArgumentException("Select at least one persistent asset.", nameof(assetIds));
        this.assetIds = Array.AsReadOnly(values);
    }
    /// <summary>Gets stable asset identities in selection order.</summary>
    public IReadOnlyList<Guid> assetIds { get; }
    /// <summary>Gets the most recently selected asset identity.</summary>
    public Guid primaryAssetId => assetIds[^1];
}
