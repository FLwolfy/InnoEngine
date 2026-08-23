using System;
using System.Linq;

using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;

using Xunit;

namespace Inno.Engine.Scene.Tests;

/// <summary>
/// Verifies runtime layer identifiers, masks, stack configuration, and Scene lookup behavior.
/// </summary>
[Collection(SceneTestsCollection.NAME)]
public sealed class LayerTests
{
    /// <summary>
    /// Verifies that layer identifiers accept exactly the thirty-two mask-backed slots.
    /// </summary>
    [Fact]
    public void Layer_ValidatesSupportedSlotRange()
    {
        Assert.Equal(0, Layer.defaultLayer.index);
        Assert.Equal(31, new Layer(31).index);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Layer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Layer(32));
    }

    /// <summary>
    /// Verifies immutable mask construction and membership operations.
    /// </summary>
    [Fact]
    public void LayerMask_ComposesAndFiltersLayers()
    {
        var first = new Layer(1);
        var second = new Layer(2);
        LayerMask mask = LayerMask.none.With(first).With(second);

        Assert.True(mask.Contains(first));
        Assert.True(mask.Contains(second));
        Assert.False(mask.Contains(Layer.defaultLayer));
        Assert.True(mask.Without(first).Contains(second));
        Assert.False(mask.Without(first).Contains(first));
        Assert.Equal(mask, LayerMask.FromLayers([first, second]));
    }

    /// <summary>
    /// Verifies layer naming constraints and symmetric interaction updates.
    /// </summary>
    [Fact]
    public void LayerStack_EnforcesUniqueDefinitionsAndSymmetricInteractions()
    {
        var stack = new LayerStack();
        var player = new Layer(1);
        var enemy = new Layer(2);
        stack.Define(player, "Player");
        stack.Define(enemy, "Enemy");
        stack.SetInteraction(player, enemy, canInteract: false);

        Assert.Equal("Default", stack.GetName(Layer.defaultLayer));
        Assert.True(stack.TryGetLayer("Player", out Layer resolved));
        Assert.Equal(player, resolved);
        Assert.False(stack.CanInteract(player, enemy));
        Assert.False(stack.CanInteract(enemy, player));
        Assert.Throws<ArgumentException>(() => stack.Define(new Layer(3), "Player"));
        Assert.Throws<InvalidOperationException>(() => stack.Remove(Layer.defaultLayer));

        LayerStack copy = stack.Clone();
        copy.SetInteraction(player, enemy, canInteract: true);
        Assert.False(stack.CanInteract(player, enemy));
        Assert.True(copy.CanInteract(player, enemy));
    }

    /// <summary>
    /// Verifies that Scene layer indexes remain coherent after live metadata changes.
    /// </summary>
    [Fact]
    public void GameScene_LayerQueriesTrackLiveObjectChanges()
    {
        var scene = new GameScene("Layers");
        GameObject first = scene.CreateObject("First");
        GameObject second = scene.CreateObject("Second");
        var player = new Layer(1);
        var enemy = new Layer(2);
        first.layer = player;
        second.layer = enemy;

        Assert.Same(first, scene.FindObjectWithLayer(player));
        Assert.Equal([first], scene.FindObjectsWithLayer(player));
        Assert.Equal(
            [first, second],
            scene.FindObjectsWithLayers(LayerMask.FromLayers([player, enemy])));

        first.layer = enemy;

        Assert.Null(scene.FindObjectWithLayer(player));
        Assert.Equal([first, second], scene.FindObjectsWithLayer(enemy));
    }
}
