using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scene;
using Inno.Scene.Layers;

using Xunit;

namespace Inno.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class EngineAssetIntegrationTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoEngineAssetTests",
        Guid.NewGuid().ToString("N"));
    private readonly SceneTestsFixture m_fixture;
    private readonly AssetPipeline m_assets;
    private readonly SerializationContext m_assetSerializationContext;
    private readonly IDisposable m_sceneScope;

    public EngineAssetIntegrationTests(SceneTestsFixture fixture)
    {
        m_fixture = fixture;
        m_sceneScope = fixture.world.EnterScope();
        string assetRoot = Path.Combine(m_root, "Assets");
        string libraryRoot = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(assetRoot);
        Directory.CreateDirectory(libraryRoot);
        AssetPipelineOptions options = AssetPipelineOptions.Create(assetRoot, libraryRoot);
        options = options with { enableFileSystemWatcher = false };
        m_assets = new AssetPipeline(
            fixture.modules,
            fixture.types,
            fixture.serialization,
            fixture.identities,
            fixture.diagnostics,
            fixture.logs,
            options);
        m_assetSerializationContext = SerializationContext.empty
            .With<IAssetReferenceResolver>(m_assets);
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        m_assets.Dispose();
        m_sceneScope.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void SceneAsset_CaptureSaveLoadInstantiate_PreservesCanonicalIdentity()
    {
        var source = new GameScene("Captured");
        source.CreateObject("Object");
        SceneAsset captured = SceneAsset.Capture(source, m_fixture.serialization, m_assets);

        Assert.True(m_assets.Save(AssetPath.Project("Scenes/sample.iscene"), captured));
        SceneAsset byPath = m_assets.Load<SceneAsset>(AssetPath.Project("Scenes/sample.iscene"));
        SceneAsset byId = m_assets.Load<SceneAsset>(byPath.identity.persistentId);
        GameScene first = byPath.Instantiate(m_fixture.serialization, m_assets);
        GameScene second = byPath.Instantiate(m_fixture.serialization, m_assets);

        Assert.Same(captured, byPath);
        Assert.Same(byPath, byId);
        Assert.Equal("sample", first.name);
        Assert.Equal("Object", Assert.Single(first.GetObjects()).name);
        Assert.NotEqual(first.identity.persistentId, second.identity.persistentId);
        Assert.NotEqual(
            Assert.Single(first.GetObjects()).identity.persistentId,
            Assert.Single(second.GetObjects()).identity.persistentId);

        DestroyScene(source);
        DestroyScene(first);
        DestroyScene(second);
    }

    [Fact]
    public void PrefabAsset_IsSceneIndependentAndRemapsInternalReferences()
    {
        var sourceScene = new GameScene("Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        GameObject sourceChild = sourceScene.CreateObject("Child");
        sourceChild.transform.SetParent(sourceRoot.transform);
        sourceRoot.AddComponent<EngineObjectReferenceComponent>().targetObject = sourceChild;
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot, m_fixture.serialization, m_assets);

        Assert.True(m_assets.Save(AssetPath.Project("Prefabs/sample.iprefab"), captured));
        PrefabAsset prefab = m_assets.Load<PrefabAsset>(AssetPath.Project("Prefabs/sample.iprefab"));
        var targetScene = new GameScene("Target");
        GameObject first = prefab.Instantiate(targetScene, m_fixture.serialization, m_assets);
        GameObject second = prefab.Instantiate(targetScene, m_fixture.serialization, m_assets);

        Assert.Same(captured, prefab);
        Assert.True(first.isPartOfPrefabInstance);
        Assert.True(first.prefabInstance!.isRoot);
        Assert.Equal(prefab.identity.persistentId, first.prefabInstance.sourceAssetId);
        Assert.Equal(first.prefabInstance.sourceObjectId, second.prefabInstance!.sourceObjectId);
        Assert.NotEqual(first.identity.persistentId, second.identity.persistentId);
        Assert.Same(
            Assert.Single(first.transform.children).gameObject,
            first.GetComponent<EngineObjectReferenceComponent>().targetObject);
        Assert.Same(
            Assert.Single(second.transform.children).gameObject,
            second.GetComponent<EngineObjectReferenceComponent>().targetObject);

        DestroyScene(sourceScene);
        DestroyScene(targetScene);
    }

    [Fact]
    public void SceneAsset_DirectAssetReferenceUsesCanonicalInstanceAndDependencyMetadata()
    {
        WriteAsset("Text/shared.txt", "shared");
        Assert.True(m_assets.Import(AssetPath.Project("Text/shared.txt")));
        TextAsset text = m_assets.Load<TextAsset>(AssetPath.Project("Text/shared.txt"));
        var sourceScene = new GameScene("Assets");
        sourceScene.CreateObject("Object")
            .AddComponent<EngineAssetReferenceComponent>()
            .asset = text;
        SceneAsset captured = SceneAsset.Capture(sourceScene, m_fixture.serialization, m_assets);

        Assert.True(m_assets.Save(AssetPath.Project("Scenes/assets.iscene"), captured));
        SceneAsset sceneAsset = m_assets.Load<SceneAsset>(AssetPath.Project("Scenes/assets.iscene"));
        AssetDependency dependency = Assert.Single(m_assets.GetDependencies(sceneAsset));
        GameScene instance = sceneAsset.Instantiate(m_fixture.serialization, m_assets);
        TextAsset restored = Assert.Single(instance.GetObjects())
            .GetComponent<EngineAssetReferenceComponent>()
            .asset!;

        Assert.Equal(text.identity.persistentId, dependency.persistentId);
        Assert.Equal("Text/shared.txt", dependency.lastKnownPath);
        Assert.Same(text, restored);

        DestroyScene(sourceScene);
        DestroyScene(instance);
    }

    [Fact]
    public void ConnectedPrefabSceneRoundtrip_PreservesPropertyAndStructureOverrides()
    {
        var sourceScene = new GameScene("Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        sourceRoot.AddComponent<EngineObjectReferenceComponent>().value = 10;
        sourceScene.CreateObject("Source Child").transform.SetParent(sourceRoot.transform);
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot, m_fixture.serialization, m_assets);
        Assert.True(m_assets.Save(AssetPath.Project("Prefabs/overrides.iprefab"), captured));
        PrefabAsset prefab = m_assets.Load<PrefabAsset>(AssetPath.Project("Prefabs/overrides.iprefab"));
        var targetScene = new GameScene("Target");
        GameObject instance = prefab.Instantiate(targetScene, m_fixture.serialization, m_assets);
        instance.GetComponent<EngineObjectReferenceComponent>().value = 42;
        Assert.True(targetScene.DestroyObject(Assert.Single(instance.transform.children).gameObject));
        targetScene.CreateObject("Added Child").transform.SetParent(instance.transform);

        byte[] bytes = m_fixture.serialization.Serialize(targetScene, m_assetSerializationContext);
        DestroyScene(targetScene);
        GameScene restored = m_fixture.serialization.Deserialize<GameScene>(bytes, m_assetSerializationContext);
        GameObject restoredRoot = restored.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);

        Assert.Equal(42, restoredRoot.GetComponent<EngineObjectReferenceComponent>().value);
        Assert.Equal("Added Child", Assert.Single(restoredRoot.transform.children).gameObject.name);
        Assert.True(restoredRoot.prefabInstance!.overrideCount >= 2);

        DestroyScene(sourceScene);
        DestroyScene(restored);
    }

    [Fact]
    public void SceneInstancesRetainSourceAssetUntilLastSceneIsUnloaded()
    {
        WeakReference sourceAsset = CreateLoadedSceneInstances();

        Assert.Equal(0, m_assets.UnloadUnusedAssets());
        Assert.True(sourceAsset.IsAlive);
        SceneManager.UnloadActiveScene();
        Assert.Equal(0, m_assets.UnloadUnusedAssets());
        Assert.True(sourceAsset.IsAlive);

        SceneManager.UnloadAllScenes();
        Assert.Equal(1, m_assets.UnloadUnusedAssets());
        Assert.False(sourceAsset.IsAlive);
    }

    [Fact]
    public void PrefabInstanceRetainsSourceAssetUntilItsSceneIsUnloaded()
    {
        WeakReference sourceAsset = CreateLoadedPrefabInstance();

        Assert.Equal(0, m_assets.UnloadUnusedAssets());
        Assert.True(sourceAsset.IsAlive);

        SceneManager.UnloadAllScenes();
        Assert.Equal(1, m_assets.UnloadUnusedAssets());
        Assert.False(sourceAsset.IsAlive);
    }

    [Fact]
    public void MissingPrefabSource_PreservesConnectionAndRecoversInPlace()
    {
        var sourceScene = new GameScene("Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        sourceScene.CreateObject("Child").transform.SetParent(sourceRoot.transform);
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot, m_fixture.serialization, m_assets);
        const string relativePath = "Prefabs/missing.iprefab";
        AssetPath assetPath = AssetPath.Project(relativePath);
        Assert.True(m_assets.Save(assetPath, captured));
        PrefabAsset prefab = m_assets.Load<PrefabAsset>(assetPath);
        var targetScene = new GameScene("Target");
        _ = prefab.Instantiate(targetScene, m_fixture.serialization, m_assets);
        byte[] sceneBytes = m_fixture.serialization.Serialize(targetScene, m_assetSerializationContext);
        string sourcePath = Path.Combine(m_root, "Assets", relativePath);
        string metaPath = sourcePath + ".imeta";
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        byte[] metaBytes = File.ReadAllBytes(metaPath);

        File.Delete(sourcePath);
        File.Delete(metaPath);
        m_assets.Rescan();
        Assert.True(prefab.isMissing);
        DestroyScene(targetScene);
        GameScene missingScene = m_fixture.serialization.Deserialize<GameScene>(sceneBytes, m_assetSerializationContext);
        GameObject missingRoot = missingScene.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.True(missingRoot.prefabInstance!.isMissing);
        byte[] preservedBytes = m_fixture.serialization.Serialize(missingScene, m_assetSerializationContext);

        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllBytes(sourcePath, sourceBytes);
        File.WriteAllBytes(metaPath, metaBytes);
        m_assets.Rescan();
        Assert.False(prefab.isMissing);
        DestroyScene(missingScene);
        GameScene recoveredScene = m_fixture.serialization.Deserialize<GameScene>(preservedBytes, m_assetSerializationContext);
        GameObject recoveredRoot = recoveredScene.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.False(recoveredRoot.prefabInstance!.isMissing);

        DestroyScene(sourceScene);
        DestroyScene(recoveredScene);
    }

    [Fact]
    public void EngineImporters_AreDiscoveredWithoutManualRegistration()
    {
        var importerTypes = m_fixture.types.GetTypesWithAttribute<AssetImporterExtensionAttribute>();

        Assert.Contains(importerTypes, type => type.Resolve(m_fixture.types).Name == "SceneAssetImporter");
        Assert.Contains(importerTypes, type => type.Resolve(m_fixture.types).Name == "PrefabAssetImporter");
        Assert.Null(typeof(AssetPipeline).GetMethod("RegisterImporter"));
    }

    [Fact]
    public void ExternalSceneRename_PreservesIdentityAndUsesFileNameAsSceneName()
    {
        var source = new GameScene("Original Name");
        source.CreateObject("Object");
        SceneAsset asset = SceneAsset.Capture(source, m_fixture.serialization, m_assets);
        Assert.True(m_assets.Save(AssetPath.Project("Scenes/old.iscene"), asset));
        Guid id = asset.identity.persistentId;
        string oldPath = Path.Combine(m_root, "Assets", "Scenes", "old.iscene");
        string newPath = Path.Combine(m_root, "Assets", "Scenes", "renamed.iscene");

        File.Move(oldPath, newPath);
        m_assets.Rescan();

        Assert.Equal(id, asset.identity.persistentId);
        Assert.Equal("Scenes/renamed.iscene", asset.assetPath.ToString());
        Assert.Same(asset, m_assets.Load<SceneAsset>(id));
        GameScene instance = asset.Instantiate(m_fixture.serialization, m_assets);
        Assert.Equal("renamed", instance.name);

        DestroyScene(source);
        DestroyScene(instance);
    }

    [Fact]
    public void ExternalPrefabRename_PreservesIdentityAndRenamesOnlyTheSourceRoot()
    {
        var sourceScene = new GameScene("Source");
        GameObject root = sourceScene.CreateObject("Old Root");
        sourceScene.CreateObject("Child").transform.SetParent(root.transform);
        PrefabAsset asset = PrefabAsset.Capture(root, m_fixture.serialization, m_assets);
        Assert.True(m_assets.Save(AssetPath.Project("Prefabs/old.iprefab"), asset));
        Guid id = asset.identity.persistentId;
        string oldPath = Path.Combine(m_root, "Assets", "Prefabs", "old.iprefab");
        string newPath = Path.Combine(m_root, "Assets", "Prefabs", "renamed.iprefab");

        File.Move(oldPath, newPath);
        m_assets.Rescan();
        var targetScene = new GameScene("Target");
        GameObject instance = asset.Instantiate(targetScene, m_fixture.serialization, m_assets);

        Assert.Equal(id, asset.identity.persistentId);
        Assert.Equal("Prefabs/renamed.iprefab", asset.assetPath.ToString());
        Assert.Equal("renamed", instance.name);
        Assert.Equal("Child", Assert.Single(instance.transform.children).gameObject.name);

        DestroyScene(sourceScene);
        DestroyScene(targetScene);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference CreateLoadedSceneInstances()
    {
        var source = new GameScene("Source");
        source.CreateObject("Object");
        SceneAsset captured = SceneAsset.Capture(source, m_fixture.serialization, m_assets);
        Assert.True(m_assets.Save(AssetPath.Project("Scenes/retained.iscene"), captured));
        SceneAsset loaded = m_assets.Load<SceneAsset>(AssetPath.Project("Scenes/retained.iscene"));
        GameScene first = loaded.Instantiate(m_fixture.serialization, m_assets);
        GameScene second = loaded.Instantiate(m_fixture.serialization, m_assets);
        DestroyScene(source);
        SceneManager.LoadScene(first);
        SceneManager.LoadSceneAdditive(second);
        return new WeakReference(loaded);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference CreateLoadedPrefabInstance()
    {
        var source = new GameScene("Source");
        GameObject root = source.CreateObject("Root");
        PrefabAsset captured = PrefabAsset.Capture(root, m_fixture.serialization, m_assets);
        Assert.True(m_assets.Save(AssetPath.Project("Prefabs/retained.iprefab"), captured));
        PrefabAsset loaded = m_assets.Load<PrefabAsset>(AssetPath.Project("Prefabs/retained.iprefab"));
        var target = new GameScene("Target");
        _ = loaded.Instantiate(target, m_fixture.serialization, m_assets);
        DestroyScene(source);
        SceneManager.LoadScene(target);
        return new WeakReference(loaded);
    }

    private static void DestroyScene(GameScene scene)
    {
        if (scene.isLoaded)
        {
            SceneManager.UnloadScene(scene);
            return;
        }

        SceneManager.LoadScene(scene);
        SceneManager.UnloadScene(scene);
    }

    private void WriteAsset(string relativePath, string content)
    {
        string path = Path.Combine(m_root, "Assets", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
