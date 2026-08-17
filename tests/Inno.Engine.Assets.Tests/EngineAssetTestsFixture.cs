using System;

using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Engine.Assets.Tests;

[CollectionDefinition(NAME)]
public sealed class EngineAssetTestsCollection : ICollectionFixture<EngineAssetTestsFixture>
{
    public const string NAME = nameof(EngineAssetTestsCollection);
}

public sealed class EngineAssetTestsFixture : IDisposable
{
    public EngineAssetTestsFixture()
    {
        _ = typeof(SceneAsset).Assembly;
        _ = typeof(EngineAssetReferenceComponent).Assembly;
        IdentityManager.Initialize();
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        IdentityManager.Shutdown();
    }
}
