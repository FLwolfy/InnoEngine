using System;
using System.Collections.Generic;

namespace Inno.Core.Graphs;

/// <summary>
/// Declares the direction in which values flow through a graph port.
/// </summary>
public enum GraphPortDirection
{
    /// <summary>
    /// Receives a value from another node.
    /// </summary>
    Input,
    /// <summary>
    /// Produces a value for another node.
    /// </summary>
    Output
}

/// <summary>
/// Declares how many edges may connect to a graph port.
/// </summary>
public enum GraphPortCapacity
{
    /// <summary>
    /// At most one edge may connect to the port.
    /// </summary>
    Single,
    /// <summary>
    /// Any number of edges may connect to the port.
    /// </summary>
    Multiple
}

/// <summary>
/// Describes one dynamically or statically resolved node port.
/// </summary>
public sealed class GraphPortDefinition
{
    /// <summary>
    /// Creates a graph port definition.
    /// </summary>
    /// <param name="id">
    /// Stable node-local port identifier.
    /// </param>
    /// <param name="displayName">
    /// Artist-facing display name.
    /// </param>
    /// <param name="valueTypeId">
    /// Stable value type identifier.
    /// </param>
    /// <param name="direction">
    /// Value-flow direction.
    /// </param>
    /// <param name="capacity">
    /// Allowed edge capacity.
    /// </param>
    /// <param name="required">
    /// Whether an input must be connected.
    /// </param>
    public GraphPortDefinition(
        GraphPortId id,
        string displayName,
        string valueTypeId,
        GraphPortDirection direction,
        GraphPortCapacity capacity = GraphPortCapacity.Single,
        bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueTypeId);
        this.id = id;
        this.displayName = displayName;
        this.valueTypeId = valueTypeId;
        this.direction = direction;
        this.capacity = capacity;
        this.required = required;
    }

    /// <summary>
    /// Gets the stable node-local port identifier.
    /// </summary>
    public GraphPortId id { get; }

    /// <summary>
    /// Gets the artist-facing display name.
    /// </summary>
    public string displayName { get; }

    /// <summary>
    /// Gets the stable value type identifier.
    /// </summary>
    public string valueTypeId { get; }

    /// <summary>
    /// Gets the value-flow direction.
    /// </summary>
    public GraphPortDirection direction { get; }

    /// <summary>
    /// Gets the allowed edge capacity.
    /// </summary>
    public GraphPortCapacity capacity { get; }

    /// <summary>
    /// Gets whether an input must be connected.
    /// </summary>
    public bool required { get; }
}

/// <summary>
/// Marks a reloadable graph node definition with a stable extension identifier.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GraphNodeExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a graph node extension declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable extension identifier.
    /// </param>
    public GraphNodeExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the globally stable extension identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Describes node presentation and resolves ports without entering graph persistence.
/// </summary>
public abstract class GraphNodeDefinition
{
    /// <summary>
    /// Creates a graph node definition.
    /// </summary>
    /// <param name="id">
    /// Globally stable definition identifier.
    /// </param>
    /// <param name="displayName">
    /// Artist-facing display name.
    /// </param>
    /// <param name="category">
    /// Search-menu category path.
    /// </param>
    protected GraphNodeDefinition(string id, string displayName, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        this.id = id;
        this.displayName = displayName;
        this.category = category;
    }

    /// <summary>
    /// Gets the globally stable definition identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the artist-facing display name.
    /// </summary>
    public string displayName { get; }

    /// <summary>
    /// Gets the search-menu category path.
    /// </summary>
    public string category { get; }

    /// <summary>
    /// Resolves ports for one node record, including any data-driven dynamic ports.
    /// </summary>
    /// <param name="node">
    /// Neutral node record to inspect.
    /// </param>
    /// <returns>
    /// The complete port definitions for the current node state.
    /// </returns>
    public abstract IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node);
}

/// <summary>
/// Resolves generation-scoped node definitions by stable identifier.
/// </summary>
public interface IGraphNodeDefinitionResolver
{
    /// <summary>
    /// Tries to resolve an active node definition.
    /// </summary>
    /// <param name="definitionId">
    /// Stable definition identifier.
    /// </param>
    /// <param name="definition">
    /// Receives the generation-scoped definition.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when active; otherwise <see langword="false"/>.
    /// </returns>
    bool TryResolve(string definitionId, out GraphNodeDefinition? definition);
}

/// <summary>
/// Determines whether one graph value type can be converted to another.
/// </summary>
public interface IGraphTypeConversion
{
    /// <summary>
    /// Tests a directed value conversion.
    /// </summary>
    /// <param name="sourceTypeId">
    /// Source value type identifier.
    /// </param>
    /// <param name="destinationTypeId">
    /// Destination value type identifier.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the conversion is valid; otherwise <see langword="false"/>.
    /// </returns>
    bool CanConvert(string sourceTypeId, string destinationTypeId);
}
