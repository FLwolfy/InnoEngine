using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.AssetType;
using Inno.Core.ECS;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.ImGui;

namespace Inno.Editor.Core;

public static class EditorSceneAssetIO
{
    private const string LOADED_SCENE_STORAGE_KEY = "SceneLoaded";
    
    /// <summary>
    /// Relative path (to AssetManager.assetDirectory) of the currently opened scene.
    /// Null when the active scene has never been opened from disk (or has no bound asset path).
    /// </summary>
    public static string? currentScenePath { get; private set; }

    public static void Initialize()
    {
        var loadedScenePath = ImGuiHost.GetStorageData<string>(LOADED_SCENE_STORAGE_KEY);
        if (loadedScenePath != null)
        {
            OpenScene(loadedScenePath);
        }
    }

    /// <summary>
    /// Save the currently opened scene to its bound asset path.
    /// This requires the scene to have been opened from disk (currentScenePath != null).
    /// </summary>
    public static bool SaveActiveScene()
    {
        if (string.IsNullOrWhiteSpace(currentScenePath))
        {
            Log.Warn("Save failed: no bound scene path. Please open a scene first (or use Save As).");
            return false;
        }

        return SaveActiveSceneAs(currentScenePath);
    }

    public static bool SaveActiveSceneAsDefaultFolder()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene == null)
        {
            Log.Warn("Save failed: no active scene.");
            return false;
        }

        string scenesDirRel = "Scenes";
        string scenesDirAbs = Path.Combine(AssetManager.assetDirectory, scenesDirRel);
        Directory.CreateDirectory(scenesDirAbs);

        string baseName = SanitizeFileName(scene.name);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Untitled";

        string candidateRel = Path.Combine(scenesDirRel, baseName + ".scene");
        string candidateAbs = Path.Combine(AssetManager.assetDirectory, candidateRel);

        int i = 1;
        while (File.Exists(candidateAbs))
        {
            candidateRel = Path.Combine(scenesDirRel, $"{baseName}_{i}.scene");
            candidateAbs = Path.Combine(AssetManager.assetDirectory, candidateRel);
            i++;
        }

        return SaveActiveSceneAs(candidateRel.Replace('\\', '/'));
    }

    public static bool SaveActiveSceneAs(string relativePath)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene == null)
        {
            Log.Warn("Save failed: no active scene.");
            return false;
        }

        if (EditorManager.mode != EditorMode.Edit)
        {
            Log.Warn("Save is only allowed in Edit mode.");
            return false;
        }

        relativePath = AssetManager.NormalizeRelativePath(relativePath);
        if (!relativePath.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
            relativePath += ".scene";

        var sceneState = ((ISerializable)scene).CaptureState();
        var sceneAsset = new SceneAsset(sceneState);

        var result = AssetManager.Save(relativePath, sceneAsset);

        if (result)
        {
            currentScenePath = relativePath; // bind path after successful save
            Log.Info($"Scene saved: {relativePath}");
            return true;
        }
        else
        {
            Log.Warn($"Scene save failed: {relativePath}");
            return false;
        }
    }

    public static bool OpenScene(string relativePath)
    {
        relativePath = AssetManager.NormalizeRelativePath(relativePath);

        // If user is playing, stop first.
        if (EditorManager.mode != EditorMode.Edit)
        {
            EditorRuntimeController.Stop();
        }

        var sceneRef = AssetManager.Get<SceneAsset>(relativePath);
        var sceneAsset = sceneRef.Resolve();
        if (sceneAsset == null)
        {
            Log.Error($"Failed to resolve scene asset: {relativePath}");
            return false;
        }

        var sceneState = SerializingState.Deserialize(sceneAsset.assetBinaries);

        var active = SceneManager.CreateScene();
        ((ISerializable)active).RestoreState(sceneState);
        SceneManager.SetActiveScene(active);

        EditorManager.selection.Deselect();

        // bind path after successful open
        currentScenePath = relativePath;
        ImGuiHost.SetStorageData(LOADED_SCENE_STORAGE_KEY, relativePath);
        Log.Info($"Scene opened: {relativePath}");
        
        return true;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Trim();
    }
}
