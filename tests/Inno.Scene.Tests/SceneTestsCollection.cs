using System;
using System.IO;

using Inno.Assets;
using Inno.Extensibility.Modules;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Scene.Tests;

[CollectionDefinition(NAME)]
public sealed class SceneTestsCollection : ICollectionFixture<SceneTestsFixture>
{
    public const string NAME = nameof(SceneTestsCollection);
}

public sealed class SceneTestsFixture : IDisposable
{
    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoSceneTests",
        Guid.NewGuid().ToString("N"));

    public ModuleHost modules { get; }
    public TypeCatalog types { get; }
    public SerializationRegistry serialization { get; }
    public IdentityAllocator identities { get; } = new();
    public DiagnosticHub diagnostics { get; } = new();
    public LogRouter logs { get; } = new();
    public Inno.Scene.SceneWorld world { get; }
    public SerializationContext serializationContext { get; }

    public SceneTestsFixture()
    {
        _ = typeof(Inno.Scene.SceneAsset);
        modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = m_cacheDirectory
        });
        types = new TypeCatalog(modules);
        serialization = new SerializationRegistry(types);
        world = new Inno.Scene.SceneWorld(identities, types);
        serializationContext = SerializationContext.empty
            .With<IAssetReferenceResolver>(new RejectingAssetReferenceResolver());
    }

    public void Dispose()
    {
        world.Dispose();
        serialization.Dispose();
        types.Dispose();
        modules.Dispose();
        logs.Dispose();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    private sealed class RejectingAssetReferenceResolver : IAssetReferenceResolver
    {
        public AssetObject Resolve(
            Guid persistentId,
            Guid stableTypeId,
            string lastKnownPath,
            Type expectedType,
            string propertyPath)
            => throw new InvalidOperationException(
                $"Unexpected asset reference '{persistentId:D}' at '{propertyPath}'.");
    }
}
