using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;

namespace Inno.Editor.Shaders;

/// <summary>Identifies a shader document and selected nodes without retaining a graph, extension object or delegate.</summary>
public sealed class ShaderInspectionSelection
{
    private readonly GraphNodeId[] m_nodes;
    /// <summary>Creates a neutral Inspector selection separate from the Asset Browser's navigation.</summary>
    /// <param name="assetId">Persistent shader asset identity.</param>
    /// <param name="nodes">Selected stable node identities; empty selects document settings.</param>
    public ShaderInspectionSelection(Guid assetId, IEnumerable<GraphNodeId> nodes)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("A persistent shader identity is required.", nameof(assetId));
        ArgumentNullException.ThrowIfNull(nodes);
        this.assetId = assetId;
        m_nodes = nodes.Distinct().OrderBy(static node => node.value, StringComparer.Ordinal).ToArray();
    }
    /// <summary>Gets the persistent shader identity.</summary>
    public Guid assetId { get; }
    /// <summary>Gets selected stable node identities.</summary>
    public IReadOnlyList<GraphNodeId> nodes => Array.AsReadOnly(m_nodes);
}
