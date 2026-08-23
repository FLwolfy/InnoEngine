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
    /// <param name="layer">The layer slot represented by the definition.</param>
    /// <param name="name">The non-empty display and lookup name.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty or contains a line break.
    /// </exception>
    public GameLayerDefinition(GameLayer layer, string name)
    {
        this.layer = layer;
        this.name = GameLayerStack.NormalizeName(name);
    }

    /// <summary>
    /// Gets the layer slot represented by this definition.
    /// </summary>
    public GameLayer layer { get; }

    /// <summary>
    /// Gets the unique ordinal layer name.
    /// </summary>
    public string name { get; }
}
