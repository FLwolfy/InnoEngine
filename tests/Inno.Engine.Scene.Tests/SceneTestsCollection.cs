using System;
using System.IO;

using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Engine.Scene.Tests;

[CollectionDefinition(NAME)]
public sealed class SceneTestsCollection : ICollectionFixture<SceneTestsFixture>
{
    public const string NAME = nameof(SceneTestsCollection);
}

public sealed class SceneTestsFixture : IDisposable
{
    public SceneTestsFixture()
    {
        IdentityManager.Initialize();
        _ = typeof(Inno.Engine.Scene.Assets.SceneAsset);
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoSceneTests", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
    }
}
