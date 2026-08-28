using System;
using System.Collections.Generic;
using System.IO;
using Inno.Core.Assemblies;
using Inno.Core.Graphs;
using Inno.Core.Reflection;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.ShaderGraph.Tests;

[Collection(ShaderNodeRegistryExtensionCollection.name)]
public sealed class ShaderNodeRegistryExtensionTests : IDisposable
{
    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoShaderNodeRegistryTests",
        Guid.NewGuid().ToString("N"));

    public ShaderNodeRegistryExtensionTests()
    {
        DisposableShaderNodeDefinition.disposeCount = 0;
        AssemblyManager.Initialize(new AssemblyManagerOptions { cacheDirectory = m_cacheDirectory });
        TypeCacheManager.Initialize();
    }

    public void Dispose()
    {
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        if (Directory.Exists(m_cacheDirectory))
        {
            Directory.Delete(m_cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public void TypeCacheRebuildAtomicallyReplacesAndDisposesNodeGeneration()
    {
        using var registry = new ShaderNodeRegistry(discoverExtensions: true);
        registry.RefreshExtensions();
        ulong firstGeneration = registry.generation;
        Assert.True(registry.TryResolveShader(DisposableShaderNodeDefinition.ID, out ShaderNodeDefinition? first));

        TypeCacheManager.Rebuild();

        Assert.True(registry.generation > firstGeneration);
        Assert.True(registry.TryResolveShader(DisposableShaderNodeDefinition.ID, out ShaderNodeDefinition? second));
        Assert.NotSame(first, second);
        Assert.Equal(1, DisposableShaderNodeDefinition.disposeCount);
        Assert.True(registry.TryResolveShader(BuiltinShaderNodes.SurfaceOutput, out _));
    }
}

[CollectionDefinition(name, DisableParallelization = true)]
public sealed class ShaderNodeRegistryExtensionCollection
{
    public const string name = "Shader node extension registry";
}

[ShaderNodeExtension(ID)]
public sealed class DisposableShaderNodeDefinition : ShaderNodeDefinition, IDisposable
{
    public const string ID = "tests.shader-node.disposable";

    public static int disposeCount;

    public DisposableShaderNodeDefinition()
        : base(ID, "Disposable Test", "Tests", ShaderStage.Fragment)
    {
    }

    public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
    {
        _ = node;
        return
        [
            new GraphPortDefinition(
                new GraphPortId("value"),
                "Value",
                ShaderGraphValueTypes.GetId(ShaderValueType.Float),
                GraphPortDirection.Output)
        ];
    }

    public override void Emit(ShaderNodeEmitContext context)
        => context.SetOutput(
            new GraphPortId("value"),
            new ShaderValue(ShaderValueType.Float, "1.0", context.node.id));

    public void Dispose()
        => disposeCount++;
}
