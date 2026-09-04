using System;

using Inno.Core.Settings;

namespace Inno.Scene.Layers;

/// <summary>
/// Describes one named layer slot in a <see cref="GameLayerStack"/> snapshot.
/// </summary>
public sealed class GameLayerDefinition
{
    /// <summary>
    /// Creates an immutable layer definition.
    /// </summary>
    /// <param name="localId">
    /// The stable project-independent identity.
    /// </param>
    /// <param name="layer">
    /// The layer slot represented by the definition.
    /// </param>
    /// <param name="name">
    /// The non-empty display and lookup name.
    /// </param>
    public GameLayerDefinition(ProjectLocalId localId, GameLayer layer, string name)
    {
        if (string.IsNullOrEmpty(localId.value))
            throw new ArgumentException("A layer definition requires a valid local identity.", nameof(localId));
        this.localId = localId;
        this.layer = layer;
        this.name = GameLayerStack.NormalizeName(name);
    }

    /// <summary>
    /// Gets the stable project-independent identity.
    /// </summary>
    public ProjectLocalId localId { get; }

    /// <summary>
    /// Gets the layer slot represented by this definition.
    /// </summary>
    public GameLayer layer { get; }

    /// <summary>
    /// Gets the unique ordinal layer name.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Resolves the complete identity under a project namespace.
    /// </summary>
    /// <param name="projectId">
    /// The current project namespace.
    /// </param>
    /// <returns>
    /// The canonical project-scoped layer identity.
    /// </returns>
    public GameLayerId GetId(ProjectId projectId)
        => new(projectId, localId);
}
