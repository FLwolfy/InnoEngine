using System;
using System.IO;
using System.Linq;
using System.Text;

using Inno.Assets;
using Inno.Assets.Types;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Engine.Assets;
using Inno.Engine.Scene;

using Xunit;

namespace Inno.Engine.Scene.Tests;

[Collection(SceneTestsCollection.NAME)]
public sealed class SceneAssetReferenceTests : IDisposable
{
    private readonly string m_root = Path.Combine(
        Path.GetTempPath(),
        "InnoSceneAssetTests",
        Guid.NewGuid().ToString("N"));

    public SceneAssetReferenceTests(SceneTestsFixture _)
    {
        string assetRoot = Path.Combine(m_root, "Assets");
        string artifactRoot = Path.Combine(m_root, "Artifacts");
        Directory.CreateDirectory(assetRoot);
        Directory.CreateDirectory(artifactRoot);
        AssetManager.Initialize(AssetManagerOptions.Create(assetRoot, artifactRoot));
    }

    public void Dispose()
    {
        SceneManager.UnloadAllScenes();
        AssetManager.Shutdown();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Fact]
    public void SceneRoundtrip_RestoresDirectAssetIdentityAndOwnerHold()
    {
        WriteAsset("Text/shared.txt", "shared");
        Assert.True(AssetManager.Import("Text/shared.txt"));
        TextAsset sourceAsset = AssetManager.Load<TextAsset>("Text/shared.txt");

        var sourceScene = new GameScene("Assets");
        AssetReferenceComponent sourceComponent =
            sourceScene.CreateObject("Object").AddComponent<AssetReferenceComponent>();
        sourceComponent.asset = sourceAsset;
        var dependencies = new AssetDependencyCollection();
        byte[] bytes = SerializationManager.Serialize(
            sourceScene,
            SerializationContext.empty.With(dependencies));

        Assert.Single(dependencies.dependencies);
        Assert.Equal(sourceAsset.identity.persistentId, dependencies.dependencies[0].persistentId);
        Assert.True(AssetManager.Unload(sourceAsset));
        SceneManager.LoadScene(sourceScene);
        Assert.True(SceneManager.UnloadScene(sourceScene));

        GameScene restored = SerializationManager.Deserialize<GameScene>(bytes);
        TextAsset restoredAsset = restored.GetObjects()
            .Single()
            .GetComponent<AssetReferenceComponent>()
            .asset!;
        TextAsset manuallyLoaded = AssetManager.Load<TextAsset>(restoredAsset.identity.persistentId);

        Assert.Same(restoredAsset, manuallyLoaded);
        Assert.True(AssetManager.Unload(manuallyLoaded));
        Assert.Single(AssetManager.GetLoadedPaths());

        SceneManager.LoadScene(restored);
        Assert.True(SceneManager.UnloadScene(restored));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void PrefabAsset_IsSceneIndependentAndInstancesKeepSourceConnection()
    {
        var sourceScene = new GameScene("Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        GameObject sourceChild = sourceScene.CreateObject("Child");
        sourceChild.transform.SetParent(sourceRoot.transform);
        ReferenceComponent reference = sourceRoot.AddComponent<ReferenceComponent>();
        reference.targetObject = sourceChild;

        PrefabAsset captured = PrefabAsset.Capture(sourceRoot);
        Assert.True(AssetManager.Save("Prefabs/sample.innoprefab", captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>("Prefabs/sample.innoprefab");
        var targetScene = new GameScene("Target");

        GameObject first = prefab.Instantiate(targetScene);
        GameObject second = prefab.Instantiate(targetScene);

        Assert.True(first.isPartOfPrefabInstance);
        Assert.True(first.prefabInstance!.isRoot);
        Assert.Equal(prefab.identity.persistentId, first.prefabInstance.sourceAssetId);
        Assert.Equal(first.prefabInstance.sourceObjectId, second.prefabInstance!.sourceObjectId);
        Assert.NotEqual(first.identity.persistentId, second.identity.persistentId);
        Assert.Same(
            Assert.Single(first.transform.children).gameObject,
            first.GetComponent<ReferenceComponent>().targetObject);
        Assert.Same(
            Assert.Single(second.transform.children).gameObject,
            second.GetComponent<ReferenceComponent>().targetObject);

        Guid prefabSourceId = first.prefabInstance.sourceAssetId;
        byte[] connectedSceneBytes = SerializationManager.Serialize(targetScene);
        Assert.True(AssetManager.Unload(prefab));
        Assert.Single(AssetManager.GetLoadedPaths());
        SceneManager.LoadScene(sourceScene);
        Assert.True(SceneManager.UnloadScene(sourceScene));
        SceneManager.LoadScene(targetScene);
        Assert.True(SceneManager.UnloadScene(targetScene));
        Assert.Empty(AssetManager.GetLoadedPaths());

        GameScene restoredScene = SerializationManager.Deserialize<GameScene>(connectedSceneBytes);
        GameObject restoredRoot = restoredScene.GetObjects()
            .First(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.Equal(prefabSourceId, restoredRoot.prefabInstance!.sourceAssetId);
        Assert.False(restoredRoot.prefabInstance.isMissing);
        Assert.Single(AssetManager.GetLoadedPaths());
        SceneManager.LoadScene(restoredScene);
        Assert.True(SceneManager.UnloadScene(restoredScene));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void ConnectedPrefabSceneRoundtrip_PreservesPropertyOverrideMetadata()
    {
        var sourceScene = new GameScene("Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        sourceRoot.AddComponent<ReferenceComponent>().value = 10;
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot);
        Assert.True(AssetManager.Save("Prefabs/overrides.innoprefab", captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>("Prefabs/overrides.innoprefab");
        var targetScene = new GameScene("Target");
        GameObject instance = prefab.Instantiate(targetScene);
        instance.GetComponent<ReferenceComponent>().value = 42;

        byte[] sceneBytes = SerializationManager.Serialize(targetScene);
        SceneManager.LoadScene(targetScene);
        Assert.True(SceneManager.UnloadScene(targetScene));
        GameScene restored = SerializationManager.Deserialize<GameScene>(sceneBytes);
        GameObject restoredRoot = restored.GetObjects().Single();

        Assert.Equal(42, restoredRoot.GetComponent<ReferenceComponent>().value);
        Assert.NotNull(restoredRoot.prefabInstance);
        Assert.True(restoredRoot.prefabInstance!.overrideCount > 0);

        SceneManager.LoadScene(sourceScene);
        Assert.True(SceneManager.UnloadScene(sourceScene));
        SceneManager.LoadScene(restored);
        Assert.True(SceneManager.UnloadScene(restored));
        Assert.True(AssetManager.Unload(prefab));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void ConnectedPrefabSceneRoundtrip_PreservesStructuralOverrides()
    {
        var sourceScene = new GameScene("Source");
        GameObject sourceRoot = sourceScene.CreateObject("Root");
        sourceRoot.AddComponent<ReferenceComponent>().value = 10;
        GameObject sourceChild = sourceScene.CreateObject("Source Child");
        sourceChild.transform.SetParent(sourceRoot.transform);
        PrefabAsset captured = PrefabAsset.Capture(sourceRoot);
        Assert.True(AssetManager.Save("Prefabs/structure.innoprefab", captured));
        PrefabAsset prefab = AssetManager.Load<PrefabAsset>("Prefabs/structure.innoprefab");
        var targetScene = new GameScene("Target");
        GameObject instance = prefab.Instantiate(targetScene);
        GameObject instantiatedChild = Assert.Single(instance.transform.children).gameObject;
        Assert.True(targetScene.DestroyObject(instantiatedChild));
        Assert.True(instance.RemoveComponent(instance.GetComponent<ReferenceComponent>()));
        instance.AddComponent<ReferenceComponent>().value = 77;
        GameObject addedChild = targetScene.CreateObject("Added Child");
        addedChild.transform.SetParent(instance.transform);

        byte[] sceneBytes = SerializationManager.Serialize(targetScene);
        SceneManager.LoadScene(targetScene);
        Assert.True(SceneManager.UnloadScene(targetScene));
        GameScene restored = SerializationManager.Deserialize<GameScene>(sceneBytes);
        GameObject restoredRoot = restored.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);

        Assert.Equal(77, restoredRoot.GetComponent<ReferenceComponent>().value);
        Assert.Equal("Added Child", Assert.Single(restoredRoot.transform.children).gameObject.name);
        Assert.True(restoredRoot.prefabInstance!.overrideCount >= 3);

        SceneManager.LoadScene(sourceScene);
        Assert.True(SceneManager.UnloadScene(sourceScene));
        SceneManager.LoadScene(restored);
        Assert.True(SceneManager.UnloadScene(restored));
        Assert.True(AssetManager.Unload(prefab));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void SceneAsset_UsesGraphPayloadAndCreatesIndependentScene()
    {
        var source = new GameScene("Captured");
        source.CreateObject("Object");
        SceneAsset captured = SceneAsset.Capture(source);
        Assert.True(AssetManager.Save("Scenes/sample.innoscene", captured));
        SceneAsset sceneAsset = AssetManager.Load<SceneAsset>("Scenes/sample.innoscene");

        GameScene instance = sceneAsset.Instantiate();
        GameScene secondInstance = sceneAsset.Instantiate();

        Assert.Equal("Captured", instance.name);
        Assert.Single(instance.GetObjects());
        Assert.Equal("Object", instance.GetObjects()[0].name);
        Assert.NotEqual(instance.identity.persistentId, secondInstance.identity.persistentId);
        Assert.NotEqual(
            instance.GetObjects()[0].identity.persistentId,
            secondInstance.GetObjects()[0].identity.persistentId);
        Assert.True(AssetManager.Unload(sceneAsset));
        Assert.Single(AssetManager.GetLoadedPaths());
        SceneManager.LoadScene(source);
        Assert.True(SceneManager.UnloadScene(source));
        SceneManager.LoadScene(instance);
        Assert.True(SceneManager.UnloadScene(instance));
        Assert.Single(AssetManager.GetLoadedPaths());
        SceneManager.LoadScene(secondInstance);
        Assert.True(SceneManager.UnloadScene(secondInstance));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void MissingPrefabSource_PreservesConnectionAndResolvesOnNextLoadAfterRecovery()
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
        byte[] sourceBytes = System.IO.File.ReadAllBytes(sourcePath);
        byte[] metaBytes = System.IO.File.ReadAllBytes(metaPath);
        byte[] artifactBytes = System.IO.File.ReadAllBytes(artifactPath);
        Assert.True(AssetManager.Unload(prefab));
        SceneManager.LoadScene(sourceScene);
        Assert.True(SceneManager.UnloadScene(sourceScene));
        SceneManager.LoadScene(targetScene);
        Assert.True(SceneManager.UnloadScene(targetScene));
        System.IO.File.Delete(sourcePath);
        System.IO.File.Delete(metaPath);
        System.IO.File.Delete(artifactPath);

        GameScene missingScene = SerializationManager.Deserialize<GameScene>(sceneBytes);
        GameObject missingRoot = missingScene.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.True(missingRoot.prefabInstance!.isMissing);
        Assert.Single(missingRoot.transform.children);
        byte[] preservedBytes = SerializationManager.Serialize(missingScene);
        SceneManager.LoadScene(missingScene);
        Assert.True(SceneManager.UnloadScene(missingScene));

        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        System.IO.File.WriteAllBytes(sourcePath, sourceBytes);
        System.IO.File.WriteAllBytes(metaPath, metaBytes);
        System.IO.File.WriteAllBytes(artifactPath, artifactBytes);
        AssetManager.Rescan();

        GameScene recoveredScene = SerializationManager.Deserialize<GameScene>(preservedBytes);
        GameObject recoveredRoot = recoveredScene.GetObjects()
            .Single(static gameObject => gameObject.prefabInstance?.isRoot == true);
        Assert.False(recoveredRoot.prefabInstance!.isMissing);
        SceneManager.LoadScene(recoveredScene);
        Assert.True(SceneManager.UnloadScene(recoveredScene));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    private void WriteAsset(string relativePath, string content)
    {
        string path = Path.Combine(m_root, "Assets", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

[StableTypeId("2d0b498f-f6be-4c0e-9c7a-96208c4fbec3")]
internal sealed class AssetReferenceComponent : GameComponent
{
    [SerializableProperty]
    public TextAsset? asset { get; set; }
}
