using System;

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
