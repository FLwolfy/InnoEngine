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
using Inno.Engine.Scene.Assets;
using Inno.Engine.Scene.Layers;

using Xunit;

namespace Inno.Engine.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class EngineAssetIntegrationTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoEngineAssetTests",
        Guid.NewGuid().ToString("N"));

    public EngineAssetIntegrationTests(SceneTestsFixture _)
    {
        string assetRoot = Path.Combine(m_root, "Assets");
        string libraryRoot = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(assetRoot);
        Directory.CreateDirectory(libraryRoot);
        AssetManagerOptions options = AssetManagerOptions.Create(assetRoot, libraryRoot);
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

        Assert.True(AssetManager.Save("Scenes/sample.iscene", captured));
        SceneAsset byPath = AssetManager.Load<SceneAsset>("Scenes/sample.iscene");
        SceneAsset byId = AssetManager.Load<SceneAsset>(byPath.identity.persistentId);
        GameScene first = byPath.Instantiate();
        GameScene second = byPath.Instantiate();

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
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot);

        Assert.True(AssetManager.Save("Prefabs/sample.iprefab", captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>("Prefabs/sample.iprefab");
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

        Assert.True(AssetManager.Save("Scenes/assets.iscene", captured));
        SceneAsset sceneAsset = AssetManager.Load<SceneAsset>("Scenes/assets.iscene");
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
        Assert.True(AssetManager.Save("Prefabs/overrides.iprefab", captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>("Prefabs/overrides.iprefab");
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
        const string relativePath = "Prefabs/missing.iprefab";
        Assert.True(AssetManager.Save(relativePath, captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>(relativePath);
        var targetScene = new GameScene("Target");
        _ = prefab.Instantiate(targetScene);
        byte[] sceneBytes = SerializationManager.Serialize(targetScene);
        string sourcePath = Path.Combine(m_root, "Assets", relativePath);
        string metaPath = sourcePath + ".imeta";
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        byte[] metaBytes = File.ReadAllBytes(metaPath);

        File.Delete(sourcePath);
        File.Delete(metaPath);
        AssetManager.Rescan();
        Assert.True(prefab.isMissing);
        DestroyScene(targetScene);
        GameScene missingScene = SerializationManager.Deserialize<GameScene>(sceneBytes);
        GameObject missingRoot = missingScene.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.True(missingRoot.prefabInstance!.isMissing);
        byte[] preservedBytes = SerializationManager.Serialize(missingScene);

        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllBytes(sourcePath, sourceBytes);
        File.WriteAllBytes(metaPath, metaBytes);
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
        var importerTypes = TypeCacheManager.GetTypesWithAttribute<AssetImporterExtensionAttribute>();

        Assert.Contains(importerTypes, type => type.Name == "SceneAssetImporter");
        Assert.Contains(importerTypes, type => type.Name == "PrefabAssetImporter");
        Assert.Contains(importerTypes, type => type.Name == "GameLayerSettingsAssetImporter");
        Assert.Null(typeof(AssetManager).GetMethod("RegisterImporter"));
    }

    [Fact]
    public void GameLayerSettingsAsset_SaveLoadAndReimport_PreservesDefinitionsAndInteractions()
    {
        GameLayerSettingsAsset created = GameLayerSettingsAsset.CreateDefault();
        var player = new Layer(1);
        var enemy = new Layer(2);
        created.layerStack.Define(player, "Player");
        created.layerStack.Define(enemy, "Enemy");
        created.layerStack.SetInteraction(player, enemy, canInteract: false);

        Assert.True(AssetManager.Save(GameLayerSettingsAsset.defaultPath, created));
        GameLayerSettingsAsset loaded = AssetManager.Load<GameLayerSettingsAsset>(
            GameLayerSettingsAsset.defaultPath);
        string sourcePath = Path.Combine(
            m_root,
            "Assets",
            GameLayerSettingsAsset.defaultPath.Replace('/', Path.DirectorySeparatorChar));
        string source = File.ReadAllText(sourcePath);

        Assert.Same(created, loaded);
        Assert.Equal("Player", loaded.layerStack.GetName(player));
        Assert.Equal("Enemy", loaded.layerStack.GetName(enemy));
        Assert.False(loaded.layerStack.CanInteract(player, enemy));
        Assert.Contains("\"layers\"", source, StringComparison.Ordinal);
        Assert.Contains("\"interactionMasks\"", source, StringComparison.Ordinal);
        Assert.True(File.Exists(sourcePath + ".imeta"));
        Assert.True(AssetManager.TryGetInfo(
            GameLayerSettingsAsset.defaultPath,
            out AssetInfo? info));
        Assert.NotNull(info);
        Assert.False(info!.artifactKey.isEmpty);
    }

    [Fact]
    public void ExternalSceneRename_PreservesIdentityAndUsesFileNameAsSceneName()
    {
        var source = new GameScene("Original Name");
        source.CreateObject("Object");
        SceneAsset asset = SceneAsset.Capture(source);
        Assert.True(AssetManager.Save("Scenes/old.iscene", asset));
        Guid id = asset.identity.persistentId;
        string oldPath = Path.Combine(m_root, "Assets", "Scenes", "old.iscene");
        string newPath = Path.Combine(m_root, "Assets", "Scenes", "renamed.iscene");

        File.Move(oldPath, newPath);
        AssetManager.Rescan();

        Assert.Equal(id, asset.identity.persistentId);
        Assert.Equal("Scenes/renamed.iscene", asset.sourcePath);
        Assert.Same(asset, AssetManager.Load<SceneAsset>(id));
        GameScene instance = asset.Instantiate();
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
        PrefabAsset asset = PrefabAsset.Capture(root);
        Assert.True(AssetManager.Save("Prefabs/old.iprefab", asset));
        Guid id = asset.identity.persistentId;
        string oldPath = Path.Combine(m_root, "Assets", "Prefabs", "old.iprefab");
        string newPath = Path.Combine(m_root, "Assets", "Prefabs", "renamed.iprefab");

        File.Move(oldPath, newPath);
        AssetManager.Rescan();
        var targetScene = new GameScene("Target");
        GameObject instance = asset.Instantiate(targetScene);

        Assert.Equal(id, asset.identity.persistentId);
        Assert.Equal("Prefabs/renamed.iprefab", asset.sourcePath);
        Assert.Equal("renamed", instance.name);
        Assert.Equal("Child", Assert.Single(instance.transform.children).gameObject.name);

        DestroyScene(sourceScene);
        DestroyScene(targetScene);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateLoadedSceneInstances()
    {
        var source = new GameScene("Source");
        source.CreateObject("Object");
        SceneAsset captured = SceneAsset.Capture(source);
        Assert.True(AssetManager.Save("Scenes/retained.iscene", captured));
        SceneAsset loaded = AssetManager.Load<SceneAsset>("Scenes/retained.iscene");
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
        Assert.True(AssetManager.Save("Prefabs/retained.iprefab", captured));
        PrefabAsset loaded = AssetManager.Load<PrefabAsset>("Prefabs/retained.iprefab");
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
