using System;
using System.Linq;

using Inno.Scene;
using Inno.Scene.Layers;

using Xunit;

namespace Inno.Scene.Tests;

/// <summary>
/// Verifies runtime layer identifiers, masks, stack configuration, and Scene lookup behavior.
/// </summary>
[Collection(SceneTestsCollection.NAME)]
public sealed class GameLayerTests : IDisposable
{
    private readonly IDisposable m_sceneScope;

    public GameLayerTests(SceneTestsFixture fixture)
    {
        m_sceneScope = fixture.world.EnterScope();
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        m_sceneScope.Dispose();
    }

    /// <summary>
    /// Verifies that layer identifiers accept exactly the thirty-two mask-backed slots.
    /// </summary>
    [Fact]
    public void GameLayer_ValidatesSupportedSlotRange()
    {
        Assert.Equal(0, GameLayer.defaultLayer.index);
        Assert.Equal(31, new GameLayer(31).index);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameLayer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameLayer(32));
    }

    /// <summary>
    /// Verifies immutable mask construction and membership operations.
    /// </summary>
    [Fact]
    public void GameLayerMask_ComposesAndFiltersLayers()
    {
        var first = new GameLayer(1);
        var second = new GameLayer(2);
        GameLayerMask mask = GameLayerMask.none.With(first).With(second);

        Assert.True(mask.Contains(first));
        Assert.True(mask.Contains(second));
        Assert.False(mask.Contains(GameLayer.defaultLayer));
        Assert.True(mask.Without(first).Contains(second));
        Assert.False(mask.Without(first).Contains(first));
        Assert.Equal(mask, GameLayerMask.FromLayers([first, second]));
    }

    /// <summary>
    /// Verifies layer naming constraints and symmetric interaction updates.
    /// </summary>
    [Fact]
    public void GameLayerStack_EnforcesUniqueDefinitionsAndSymmetricInteractions()
    {
        var stack = new GameLayerStack();
        var player = new GameLayer(1);
        var enemy = new GameLayer(2);
        stack.Define(player, "Player");
        stack.Define(enemy, "Enemy");
        stack.SetInteraction(player, enemy, canInteract: false);

        Assert.Equal("Default", stack.GetName(GameLayer.defaultLayer));
        Assert.True(stack.TryGetLayer("Player", out GameLayer resolved));
        Assert.Equal(player, resolved);
        Assert.False(stack.CanInteract(player, enemy));
        Assert.False(stack.CanInteract(enemy, player));
        Assert.Throws<ArgumentException>(() => stack.Define(new GameLayer(3), "Player"));
        Assert.Throws<InvalidOperationException>(() => stack.Remove(GameLayer.defaultLayer));

        GameLayerStack copy = stack.Clone();
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
        var player = new GameLayer(1);
        var enemy = new GameLayer(2);
        first.layer = player;
        second.layer = enemy;

        Assert.Same(first, scene.FindObjectWithLayer(player));
        Assert.Equal([first], scene.FindObjectsWithLayer(player));
        Assert.Equal(
            [first, second],
            scene.FindObjectsWithLayers(GameLayerMask.FromLayers([player, enemy])));

        first.layer = enemy;

        Assert.Null(scene.FindObjectWithLayer(player));
        Assert.Equal([first, second], scene.FindObjectsWithLayer(enemy));
    }
}
