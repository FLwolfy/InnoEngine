using System;

namespace Inno.Core.Graphs;

/// <summary>
/// Identifies a node within one graph document using a stable serialized value.
/// </summary>
public readonly record struct GraphNodeId
{
    /// <summary>
    /// Creates a stable graph node identifier.
    /// </summary>
    /// <param name="value">Non-empty identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public GraphNodeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the serialized identifier value.
    /// </summary>
    public string value { get; }

    /// <inheritdoc />
    public override string ToString() => value;
}

/// <summary>
/// Identifies an edge within one graph document using a stable serialized value.
/// </summary>
public readonly record struct GraphEdgeId
{
    /// <summary>
    /// Creates a stable graph edge identifier.
    /// </summary>
    /// <param name="value">Non-empty identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public GraphEdgeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the serialized identifier value.
    /// </summary>
    public string value { get; }

    /// <inheritdoc />
    public override string ToString() => value;
}

/// <summary>
/// Identifies a port within its owning node definition.
/// </summary>
public readonly record struct GraphPortId
{
    /// <summary>
    /// Creates a stable graph port identifier.
    /// </summary>
    /// <param name="value">Non-empty identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public GraphPortId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>
    /// Gets the serialized identifier value.
    /// </summary>
    public string value { get; }

    /// <inheritdoc />
    public override string ToString() => value;
}

/// <summary>
/// Stores a graph-space position independently from any editor UI framework.
/// </summary>
public readonly record struct GraphPosition
{
    /// <summary>
    /// Creates a graph-space position.
    /// </summary>
    /// <param name="x">Horizontal graph-space coordinate.</param>
    /// <param name="y">Vertical graph-space coordinate.</param>
    public GraphPosition(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    /// <summary>
    /// Gets the horizontal graph-space coordinate.
    /// </summary>
    public float x { get; }

    /// <summary>
    /// Gets the vertical graph-space coordinate.
    /// </summary>
    public float y { get; }
}
