using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Scene;

using Xunit;

namespace Inno.Engine.Assets.Tests;

[Collection(EngineAssetTestsCollection.NAME)]
public sealed class EngineAssetIntegrationTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoEngineAssetTests",
        Guid.NewGuid().ToString("N"));

    public EngineAssetIntegrationTests(EngineAssetTestsFixture _)
    {
        string assetRoot = Path.Combine(m_root, "Assets");
        string artifactRoot = Path.Combine(m_root, "Artifacts");
        Directory.CreateDirectory(assetRoot);
        Directory.CreateDirectory(artifactRoot);
        AssetManagerOptions options = AssetManagerOptions.Create(assetRoot, artifactRoot);
        options = options with { enableFileSystemWatcher = false };
        AssetManager.Initialize(options);
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        AssetManager.Shutdown();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void SceneAsset_CaptureSaveLoadInstantiate_PreservesCanonicalIdentity()
    {
        var source = new GameScene("Captured");
        source.CreateObject("Object");
        SceneAsset captured = SceneAsset.Capture(source);

        Assert.True(AssetManager.Save("Scenes/sample.innoscene", captured));
        SceneAsset byPath = AssetManager.Load<SceneAsset>("Scenes/sample.innoscene");
        SceneAsset byId = AssetManager.Load<SceneAsset>(byPath.identity.persistentId);
        GameScene first = byPath.Instantiate();
        GameScene second = byPath.Instantiate();

        Assert.Same(captured, byPath);
        Assert.Same(byPath, byId);
        Assert.Equal("Captured", first.name);
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
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot);

        Assert.True(AssetManager.Save("Prefabs/sample.innoprefab", captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>("Prefabs/sample.innoprefab");
        var targetScene = new GameScene("Target");
        GameObject first = prefab.Instantiate(targetScene);
        GameObject second = prefab.Instantiate(targetScene);

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
        Assert.True(AssetManager.Import("Text/shared.txt"));
        TextAsset text = AssetManager.Load<TextAsset>("Text/shared.txt");
        var sourceScene = new GameScene("Assets");
        sourceScene.CreateObject("Object")
            .AddComponent<EngineAssetReferenceComponent>()
            .asset = text;
        SceneAsset captured = SceneAsset.Capture(sourceScene);

        Assert.True(AssetManager.Save("Scenes/assets.innoscene", captured));
        SceneAsset sceneAsset = AssetManager.Load<SceneAsset>("Scenes/assets.innoscene");
        AssetDependency dependency = Assert.Single(AssetManager.GetDependencies(sceneAsset));
        GameScene instance = sceneAsset.Instantiate();
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
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot);
        Assert.True(AssetManager.Save("Prefabs/overrides.innoprefab", captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>("Prefabs/overrides.innoprefab");
        var targetScene = new GameScene("Target");
        GameObject instance = prefab.Instantiate(targetScene);
        instance.GetComponent<EngineObjectReferenceComponent>().value = 42;
        Assert.True(targetScene.DestroyObject(Assert.Single(instance.transform.children).gameObject));
        targetScene.CreateObject("Added Child").transform.SetParent(instance.transform);

        byte[] bytes = SerializationManager.Serialize(targetScene);
        DestroyScene(targetScene);
        GameScene restored = SerializationManager.Deserialize<GameScene>(bytes);
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

        Assert.Equal(0, AssetManager.UnloadUnusedAssets());
        Assert.True(sourceAsset.IsAlive);
        SceneManager.UnloadActiveScene();
        Assert.Equal(0, AssetManager.UnloadUnusedAssets());
        Assert.True(sourceAsset.IsAlive);

        SceneManager.UnloadAllScenes();
        Assert.Equal(1, AssetManager.UnloadUnusedAssets());
        Assert.False(sourceAsset.IsAlive);
    }

    [Fact]
    public void PrefabInstanceRetainsSourceAssetUntilItsSceneIsUnloaded()
    {
        WeakReference sourceAsset = CreateLoadedPrefabInstance();

        Assert.Equal(0, AssetManager.UnloadUnusedAssets());
        Assert.True(sourceAsset.IsAlive);

        SceneManager.UnloadAllScenes();
        Assert.Equal(1, AssetManager.UnloadUnusedAssets());
        Assert.False(sourceAsset.IsAlive);
    }

    [Fact]
    public void MissingPrefabSource_PreservesConnectionAndRecoversInPlace()
    {
        var sourceScene = new GameScene("Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        sourceScene.CreateObject("Child").transform.SetParent(sourceRoot.transform);
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot);
        const string relativePath = "Prefabs/missing.innoprefab";
        Assert.True(AssetManager.Save(relativePath, captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>(relativePath);
        var targetScene = new GameScene("Target");
        _ = prefab.Instantiate(targetScene);
        byte[] sceneBytes = SerializationManager.Serialize(targetScene);
        string sourcePath = Path.Combine(m_root, "Assets", relativePath);
        string metaPath = sourcePath + ".imeta";
        string artifactPath = Path.Combine(m_root, "Artifacts", relativePath + ".abin");
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        byte[] metaBytes = File.ReadAllBytes(metaPath);
        byte[] artifactBytes = File.ReadAllBytes(artifactPath);

        File.Delete(sourcePath);
        File.Delete(metaPath);
        File.Delete(artifactPath);
        AssetManager.Rescan();
        Assert.True(prefab.isMissing);
        DestroyScene(targetScene);
        GameScene missingScene = SerializationManager.Deserialize<GameScene>(sceneBytes);
        GameObject missingRoot = missingScene.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.True(missingRoot.prefabInstance!.isMissing);
        byte[] preservedBytes = SerializationManager.Serialize(missingScene);

        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllBytes(sourcePath, sourceBytes);
        File.WriteAllBytes(metaPath, metaBytes);
        File.WriteAllBytes(artifactPath, artifactBytes);
        AssetManager.Rescan();
        Assert.False(prefab.isMissing);
        DestroyScene(missingScene);
        GameScene recoveredScene = SerializationManager.Deserialize<GameScene>(preservedBytes);
        GameObject recoveredRoot = recoveredScene.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.False(recoveredRoot.prefabInstance!.isMissing);

        DestroyScene(sourceScene);
        DestroyScene(recoveredScene);
    }

    [Fact]
    public void EngineImporters_AreDiscoveredWithoutManualRegistration()
    {
        var importerTypes = TypeCache.GetTypesWithAttribute<AssetImporterExtensionAttribute>();

        Assert.Contains(importerTypes, type => type.Name == "SceneAssetImporter");
        Assert.Contains(importerTypes, type => type.Name == "PrefabAssetImporter");
        Assert.Null(typeof(AssetManager).GetMethod("RegisterImporter"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateLoadedSceneInstances()
    {
        var source = new GameScene("Source");
        source.CreateObject("Object");
        SceneAsset captured = SceneAsset.Capture(source);
        Assert.True(AssetManager.Save("Scenes/retained.innoscene", captured));
        SceneAsset loaded = AssetManager.Load<SceneAsset>("Scenes/retained.innoscene");
        GameScene first = loaded.Instantiate();
        GameScene second = loaded.Instantiate();
        DestroyScene(source);
        SceneManager.LoadScene(first);
        SceneManager.LoadSceneAdditive(second);
        return new WeakReference(loaded);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateLoadedPrefabInstance()
    {
        var source = new GameScene("Source");
        GameObject root = source.CreateObject("Root");
        PrefabAsset captured = PrefabAsset.Capture(root);
        Assert.True(AssetManager.Save("Prefabs/retained.innoprefab", captured));
        PrefabAsset loaded = AssetManager.Load<PrefabAsset>("Prefabs/retained.innoprefab");
        var target = new GameScene("Target");
        _ = loaded.Instantiate(target);
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
