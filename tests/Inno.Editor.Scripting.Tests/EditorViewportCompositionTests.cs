using System;
using System.Collections.Generic;

using Inno.Editor.Rendering;
using Inno.Rendering;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class EditorViewportCompositionTests
{
    [Fact]
    public void CompositionCanonicalizesModelLayersByOrderAndStableIdentity()
    {
        var overlay = new EditorViewportLayer("tests.overlay", null, new RenderFrameData(), 1000);
        var baseB = new EditorViewportLayer("tests.base-b", null, new RenderFrameData(), 0);
        var baseA = new EditorViewportLayer("tests.base-a", null, new RenderFrameData(), 0);

        var composition = new EditorViewportComposition(
            "tests.scene-view",
            1280,
            720,
            RenderTextureFormat.RGBA8Srgb,
            new[] { overlay, baseB, baseA });

        Assert.Collection(
            composition.layers,
            layer => Assert.Same(baseA, layer),
            layer => Assert.Same(baseB, layer),
            layer => Assert.Same(overlay, layer));
    }

    [Fact]
    public void CompositionSnapshotsTheCallerCollection()
    {
        var retained = new EditorViewportLayer("tests.retained", null, new RenderFrameData(), 0);
        var source = new List<EditorViewportLayer> { retained };
        var composition = new EditorViewportComposition(
            "tests.game-view",
            640,
            360,
            RenderTextureFormat.RGBA8Srgb,
            source);

        source.Clear();

        Assert.Single(composition.layers);
        Assert.Same(retained, composition.layers[0]);
        Assert.False(composition.layers is EditorViewportLayer[]);
    }

    [Fact]
    public void CompositionRejectsEmptyNullAndDuplicateModelLayers()
    {
        Assert.Throws<ArgumentException>(() => new EditorViewportComposition(
            "tests.empty",
            64,
            64,
            RenderTextureFormat.RGBA8Srgb,
            Array.Empty<EditorViewportLayer>()));
        Assert.Throws<ArgumentException>(() => new EditorViewportComposition(
            "tests.null",
            64,
            64,
            RenderTextureFormat.RGBA8Srgb,
            new EditorViewportLayer[] { null! }));

        var first = new EditorViewportLayer("tests.duplicate", null, new RenderFrameData(), 0);
        var second = new EditorViewportLayer("tests.duplicate", null, new RenderFrameData(), 1000);
        Assert.Throws<ArgumentException>(() => new EditorViewportComposition(
            "tests.duplicate",
            64,
            64,
            RenderTextureFormat.RGBA8Srgb,
            new[] { first, second }));
    }
}
