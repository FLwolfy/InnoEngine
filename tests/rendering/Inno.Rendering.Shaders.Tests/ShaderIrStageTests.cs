using System;
using System.Collections.Generic;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderIrStageTests
{
    [Fact]
    public void SemanticHashCoversNestedCodeStorageAccessBindingsAndWorkgroups()
    {
        string Hash(float value, int slot, int threads, RenderStorageAccess access)
        {
            var builder = new ShaderIrBuilder();
            ShaderSourceType resourceType = ShaderSourceType.Storage(ShaderStorageType.Buffer(ShaderSourceType.Atomic("float"), access));
            ShaderIrValue resource = builder.Input("resource", resourceType);
            ShaderIrValue condition = builder.Constant(true);
            _ = builder.Branch(condition, region =>
            {
                region.StoreStorage(resource, region.Constant(0u), region.Constant(value));
                return new Dictionary<string, ShaderIrValue>();
            }, region => new Dictionary<string, ShaderIrValue>());
            return new ShaderIrStage(ShaderStage.Compute, builder.Build(new Dictionary<string, ShaderIrValue>()),
                [new("resource", resourceType, ShaderIrInputKind.Storage, location: slot)], [], threads).contentHash;
        }
        string first = Hash(1f, 0, 8, RenderStorageAccess.ReadWrite);
        Assert.Equal(first, Hash(1f, 0, 8, RenderStorageAccess.ReadWrite));
        Assert.NotEqual(first, Hash(2f, 0, 8, RenderStorageAccess.ReadWrite));
        Assert.NotEqual(first, Hash(1f, 1, 8, RenderStorageAccess.ReadWrite));
        Assert.NotEqual(first, Hash(1f, 0, 16, RenderStorageAccess.ReadWrite));
        Assert.NotEqual(first, Hash(1f, 0, 8, RenderStorageAccess.Write));
    }

    [Fact]
    public void VertexStageRequiresClipPositionAndExactBodyInterface()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrValue position = builder.Input("position", ShaderSourceType.Atomic("float4"));
        ShaderIrBlock body = builder.Build(new Dictionary<string, ShaderIrValue> { ["clip"] = position });
        ShaderIrStageInput input = new("position", position.type, ShaderIrInputKind.VertexAttribute, "position");
        ShaderIrStageOutput output = new("clip", ShaderIrOutputKind.ClipPosition);
        var stage = new ShaderIrStage(ShaderStage.Vertex, body, [input], [output]);
        Assert.Same(body, stage.body);
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Vertex, body, [], [output]));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Vertex, body, [input], []));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Vertex, body, [input], [new("clip", ShaderIrOutputKind.Varying, "texcoord")]));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Fragment, body, [input], [new("clip", ShaderIrOutputKind.Color)]));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Vertex, body, [input], [output], threadsX: 2));
    }

    [Fact]
    public void AliasedResourceSlotsAndUnmappedOutputsAreRejected()
    {
        var builder = new ShaderIrBuilder();
        _ = builder.Input("first", ShaderSourceType.Atomic("sampled-texture2d"));
        _ = builder.Input("second", ShaderSourceType.Atomic("sampled-texture2d"));
        ShaderIrBlock body = builder.Build(new Dictionary<string, ShaderIrValue>());
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Fragment, body,
            [new("first", ShaderSourceType.Atomic("sampled-texture2d"), ShaderIrInputKind.SampledTexture, "ignored-1", 2),
                new("second", ShaderSourceType.Atomic("sampled-texture2d"), ShaderIrInputKind.SampledTexture, "ignored-2", 2)], []));
    }

    [Fact]
    public void ComputeCannotPretendToBeRasterAndStageCopiesInterfaceLists()
    {
        var builder = new ShaderIrBuilder();
        ShaderIrBlock body = builder.Build(new Dictionary<string, ShaderIrValue>());
        var inputs = new List<ShaderIrStageInput>();
        var outputs = new List<ShaderIrStageOutput>();
        var stage = new ShaderIrStage(ShaderStage.Compute, body, inputs, outputs, 8, 4, 2);
        inputs.Add(new("late", ShaderSourceType.Atomic("float"), ShaderIrInputKind.Uniform));
        Assert.Empty(stage.inputs);
        Assert.Equal(8, stage.threadsX);
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Compute, body, [], [], 0));
        Assert.Throws<ArgumentException>(() => new ShaderIrStage(ShaderStage.Vertex | ShaderStage.Fragment, body, [], []));
    }
}
