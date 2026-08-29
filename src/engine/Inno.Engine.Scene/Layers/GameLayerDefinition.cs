using System;

namespace Inno.Engine.Scene.Layers;

/// <summary>
/// Describes one named layer slot in a <see cref="GameLayerStack"/> snapshot.
/// </summary>
public sealed class GameLayerDefinition
{
    /// <summary>
    /// Creates an immutable layer definition.
    /// </summary>
    /// <param name="id">The globally stable logical identity.</param>
    /// <param name="layer">The layer slot represented by the definition.</param>
    /// <param name="name">The non-empty display and lookup name.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty or contains a line break.
    /// </exception>
    public GameLayerDefinition(GameLayerId id, GameLayer layer, string name)
    {
        if (!id.isValid)
            throw new ArgumentException("A layer definition requires a valid logical ID.", nameof(id));
        this.id = id;
        this.layer = layer;
        this.name = GameLayerStack.NormalizeName(name);
    }

    /// <summary>Gets the globally stable logical identity.</summary>
    public GameLayerId id { get; }

    /// <summary>
    /// Gets the layer slot represented by this definition.
    /// </summary>
    public GameLayer layer { get; }

    /// <summary>
    /// Gets the unique ordinal layer name.
    /// </summary>
    public string name { get; }
}
