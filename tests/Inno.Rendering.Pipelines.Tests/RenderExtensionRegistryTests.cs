using System;
using System.Collections.Generic;
using System.Text.Json;

using Xunit;

namespace Inno.Rendering.Pipelines.Tests;

public sealed class RenderExtensionRegistryTests
{
    [Fact]
    public void SnapshotCreatesConfiguredGenerationFromStableIds()
    {
        var snapshot = CreateSnapshot();
        var asset = new RenderPipelineAsset { pipelineTypeId = "tests.pipeline" };
        asset.SetFeatures([new RenderFeatureConfiguration("tests.feature", "{\"strength\":0.75}")]);

        using RenderExtensionRegistry.Generation generation = snapshot.CreateGeneration(asset);

        Assert.IsType<TestPipeline>(generation.pipeline);
        Assert.IsType<TestFeature>(Assert.Single(generation.features).Value);
    }

    [Fact]
    public void MissingFeatureDisposesCandidateAndKeepsFailureIsolated()
    {
        TestPipeline.disposeCount = 0;
        var snapshot = CreateSnapshot();
        var asset = new RenderPipelineAsset { pipelineTypeId = "tests.pipeline" };
        asset.SetFeatures([new RenderFeatureConfiguration("tests.missing")]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => snapshot.CreateGeneration(asset));

        Assert.Contains("tests.missing", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, TestPipeline.disposeCount);
    }

    [Fact]
    public void LayerReplacementConfiguresCandidateThenDisposesPreviousGeneration()
    {
        TestPipeline.disposeCount = 0;
        TestFeature.disposeCount = 0;
        TestFeature.configuredStrength = 0f;
        var oldPipeline = new TestPipeline();
        var oldFeature = new TestFeature();
        var oldAsset = new RenderPipelineAsset();
        oldAsset.SetFeatures([new RenderFeatureConfiguration("tests.feature")]);
        var layer = new RenderingLayer(
            new RenderingLayerTests.RecordingDevice(),
            oldAsset,
            oldPipeline,
            new RenderingLayerTests.EmptyExecutor(),
            new RenderingLayerTests.RecordingDiagnostics(),
            new Dictionary<string, RenderPipelineFeature>(StringComparer.Ordinal)
            {
                ["tests.feature"] = oldFeature
            });
        var newAsset = new RenderPipelineAsset();
        newAsset.SetFeatures([new RenderFeatureConfiguration("tests.feature", "{\"strength\":0.5}")]);
        var newFeature = new TestFeature();

        layer.ReplaceGeneration(
            newAsset,
            new TestPipeline(),
            new Dictionary<string, RenderPipelineFeature>(StringComparer.Ordinal)
            {
                ["tests.feature"] = newFeature
            });

        Assert.Equal(1, TestPipeline.disposeCount);
        Assert.Equal(1, TestFeature.disposeCount);
        Assert.Equal(0.5f, TestFeature.configuredStrength);
        layer.OnDetach();
    }

    private static RenderExtensionRegistry.Snapshot CreateSnapshot()
        => new(
            7,
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["tests.pipeline"] = typeof(TestPipeline)
            },
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["tests.feature"] = typeof(TestFeature)
            });

    private sealed class TestPipeline : RenderPipeline
    {
        internal static int disposeCount;

        public override void Build(RenderPipelineContext context)
            => _ = context;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                disposeCount++;
        }
    }

    private sealed class TestFeature : RenderPipelineFeature, IDisposable
    {
        internal static int disposeCount;
        internal static float configuredStrength;

        public override void AddRenderPasses(RenderFeatureContext context)
            => _ = context;

        public void Dispose()
            => disposeCount++;

        protected override void OnConfigure(JsonElement settings)
            => configuredStrength = settings.TryGetProperty("strength", out JsonElement value)
                ? value.GetSingle()
                : 0f;
    }
}
