using Xunit;

namespace Inno.Engine.Scene.Tests;

[CollectionDefinition(NAME)]
public sealed class SceneTestsCollection : ICollectionFixture<SceneTestsFixture>
{
    public const string NAME = nameof(SceneTestsCollection);
}

public sealed class SceneTestsFixture
{
}
