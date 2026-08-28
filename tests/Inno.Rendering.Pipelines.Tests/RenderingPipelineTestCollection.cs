using System;
using System.IO;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Xunit;

namespace Inno.Rendering.Pipelines.Tests;

[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class RenderingPipelineTestCollection : ICollectionFixture<RenderingPipelineTestFixture>
{
    public const string NAME = nameof(RenderingPipelineTestCollection);
}

public sealed class RenderingPipelineTestFixture : IDisposable
{
    public RenderingPipelineTestFixture()
    {
        IdentityManager.Initialize();
        _ = typeof(Camera);
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoRenderingPipelineTests", "Assemblies")
        });
        TypeCacheManager.Initialize();
    }

    public void Dispose()
    {
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
    }
}
