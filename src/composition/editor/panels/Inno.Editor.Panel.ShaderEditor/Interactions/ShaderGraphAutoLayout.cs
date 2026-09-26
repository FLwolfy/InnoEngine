using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

/// <summary>
/// Creates a deterministic left-to-right authoring layout without changing shader semantics.
/// </summary>
internal static class ShaderGraphAutoLayout
{
    private const float C_COLUMN = 350f;
    private const float C_ROW = 190f;
    private const float C_LANE = 110f;
    private const int C_SWEEPS = 10;

    internal static GraphDocument Apply(
        GraphDocument source,
        SerializationRegistry serialization,
        SerializationContext context,
        Func<GraphNodeRecord, IReadOnlyList<ShaderNodePort>> describePorts)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(describePorts);
        GraphDocument graph = source.Clone();
        var placed = new HashSet<GraphNodeId>();
        float laneY = 70f;
        GraphNodeRecord[] outputs = graph.nodes.Where(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .OrderBy(node => ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), serialization, context).stage)
            .ThenBy(static node => node.id.value, StringComparer.Ordinal).ToArray();
        if (outputs.Length == 0)
            outputs = graph.nodes.Where(static node => node.definitionId == ShaderGraphNodes.outputDefinitionId
                    || node.definitionId.EndsWith("-output", StringComparison.Ordinal))
                .OrderBy(static node => node.id.value, StringComparer.Ordinal).ToArray();
        foreach (GraphNodeRecord output in outputs)
        {
            GraphNodeRecord[] members = output.definitionId == ShaderGraphDocument.outputDefinitionId
                ? graph.nodes.Where(node => node.id == output.id
                    || ShaderGraphDocument.Read(node, "stage", "", serialization, context) == output.id.value).ToArray()
                : graph.nodes.Where(node => !placed.Contains(node.id)).ToArray();
            LayoutLane(output, members, ref laneY);
        }
        GraphNodeRecord[] remaining = graph.nodes.Where(node => !placed.Contains(node.id))
            .OrderBy(static node => node.position.y).ThenBy(static node => node.id.value, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < remaining.Length; i++)
        {
            remaining[i].position = new((i % 4) * C_COLUMN + 70f, laneY + i / 4 * C_ROW);
            placed.Add(remaining[i].id);
        }
        return graph;

        void LayoutLane(GraphNodeRecord output, GraphNodeRecord[] members, ref float y)
        {
            var memberIds = members.Select(static node => node.id).ToHashSet();
            var distance = new Dictionary<GraphNodeId, int> { [output.id] = 0 };
            var queue = new Queue<GraphNodeId>();
            queue.Enqueue(output.id);
            while (queue.Count != 0)
            {
                GraphNodeId current = queue.Dequeue();
                foreach (GraphNodeId predecessor in graph.edges.Where(edge => edge.input.nodeId == current && memberIds.Contains(edge.output.nodeId))
                             .Select(static edge => edge.output.nodeId).Distinct())
                {
                    int next = distance[current] + 1;
                    // A draft may be invalid and contain a cycle. Layout must still terminate;
                    // compilation owns cycle diagnostics, while the first reverse-BFS rank gives
                    // every reachable node a stable visual column.
                    if (distance.ContainsKey(predecessor)) continue;
                    distance[predecessor] = next;
                    queue.Enqueue(predecessor);
                }
            }

            int maximumDistance = Math.Max(1, distance.Values.DefaultIfEmpty().Max());
            Dictionary<int, List<GraphNodeRecord>> ranks = members.Where(node => distance.ContainsKey(node.id))
                .GroupBy(node => distance[node.id])
                .ToDictionary(static group => group.Key, static group => group
                    .OrderBy(static node => node.position.y)
                    .ThenBy(static node => node.id.value, StringComparer.Ordinal).ToList());

            // Alternating barycentric sweeps are the crossing-reduction phase of a layered graph
            // layout. Port order participates in the score, so values feeding ordered inputs do
            // not get bundled into visibly crossing curves.
            for (int sweep = 0; sweep < C_SWEEPS; sweep++)
            {
                for (int rank = 1; rank <= maximumDistance; rank++) Order(rank, towardOutput: true);
                for (int rank = maximumDistance - 1; rank >= 0; rank--) Order(rank, towardOutput: false);
            }

            int maximumRows = ranks.Values.Select(static rank => rank.Count).DefaultIfEmpty(1).Max();
            foreach ((int rank, List<GraphNodeRecord> nodes) in ranks)
            {
                int visualColumn = maximumDistance + 1 - rank;
                for (int row = 0; row < nodes.Count; row++)
                {
                    nodes[row].position = new(70f + visualColumn * C_COLUMN, y + row * C_ROW);
                    placed.Add(nodes[row].id);
                }
            }
            y += maximumRows * C_ROW + C_LANE;

            void Order(int rank, bool towardOutput)
            {
                if (!ranks.TryGetValue(rank, out List<GraphNodeRecord>? nodes) || nodes.Count < 2) return;
                var oldOrder = nodes.Select(static (node, index) => (node.id, index)).ToDictionary();
                nodes.Sort((left, right) =>
                {
                    int comparison = Score(left, towardOutput).CompareTo(Score(right, towardOutput));
                    if (comparison != 0) return comparison;
                    comparison = oldOrder[left.id].CompareTo(oldOrder[right.id]);
                    return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.id.value, right.id.value);
                });
            }

            double Score(GraphNodeRecord node, bool towardOutput)
            {
                IEnumerable<(GraphNodeId neighbor, GraphPortId port)> endpoints = towardOutput
                    ? graph.edges.Where(edge => edge.output.nodeId == node.id && distance.ContainsKey(edge.input.nodeId))
                        .Select(static edge => (edge.input.nodeId, edge.input.portId))
                    : graph.edges.Where(edge => edge.input.nodeId == node.id && distance.ContainsKey(edge.output.nodeId))
                        .Select(static edge => (edge.output.nodeId, edge.output.portId));
                double total = 0;
                int count = 0;
                foreach ((GraphNodeId neighborId, GraphPortId port) in endpoints)
                {
                    if (!distance.TryGetValue(neighborId, out int neighborRank)
                        || !ranks.TryGetValue(neighborRank, out List<GraphNodeRecord>? neighbors)) continue;
                    int index = neighbors.FindIndex(candidate => candidate.id == neighborId);
                    if (index < 0) continue;
                    IReadOnlyList<ShaderNodePort> ports;
                    try { ports = describePorts(neighbors[index]); }
                    catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or FormatException)
                    { ports = Array.Empty<ShaderNodePort>(); }
                    GraphPortDirection direction = towardOutput ? GraphPortDirection.Input : GraphPortDirection.Output;
                    ShaderNodePort[] matching = ports.Where(candidate => candidate.direction == direction).ToArray();
                    int portIndex = Array.FindIndex(matching, candidate => candidate.id == port.value);
                    double portOffset = portIndex < 0 || matching.Length < 2 ? 0d
                        : ((double)portIndex / (matching.Length - 1) - 0.5d) * 0.8d;
                    total += index + portOffset;
                    count++;
                }
                return count == 0 ? node.position.y : total / count;
            }
        }
    }
}
