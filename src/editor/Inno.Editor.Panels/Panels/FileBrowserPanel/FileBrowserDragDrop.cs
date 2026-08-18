using System;
using System.IO;

using Inno.Assets;
using Inno.Assets.File;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Engine.Scene;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

internal sealed class FileBrowserDragDrop
{
    private const string C_ASSET_PAYLOAD = "INNO_ASSET";
    private const string C_SCENE_PAYLOAD = "INNO_SCENE";
    private const string C_SCENE_OBJECT_PAYLOAD = "INNO_SCENE_OBJECT";

    internal void DrawAssetSource(AssetFileEntry entry)
    {
        if (entry.isDirectory)
            return;

        _ = ImGuiWidget.DragDropSource<Guid>(
            C_ASSET_PAYLOAD,
            () => AssetManager.TryGetPersistentId(entry.relativePath, out Guid persistentId)
                ? persistentId
                : Guid.Empty,
            () => NativeImGui.TextUnformatted(Path.GetFileName(entry.relativePath)));
    }

    internal void DrawSceneAssetTarget(EditorContext context)
    {
        if (ImGuiWidget.DragDropTarget<Guid>(C_SCENE_PAYLOAD, out Guid sceneId))
        {
            GameScene? scene = IdentityManager.Get<GameScene>(sceneId);
            if (scene is not null)
                SaveScene(context, scene);
            return;
        }

        if (ImGuiWidget.DragDropTarget<Guid>(C_SCENE_OBJECT_PAYLOAD, out Guid objectId))
        {
            GameObject? gameObject = IdentityManager.Get<GameObject>(objectId);
            if (gameObject is not null && gameObject.isRuntimeValid)
                SavePrefab(context, gameObject);
        }
    }

    private static void SaveScene(EditorContext context, GameScene scene)
    {
        try
        {
            string path = context.sceneWorkspace.SaveSceneToDirectory(scene, context.selection.currentDirectory);
            context.selection.SetSelectedPath(path);
        }
        catch (Exception exception)
        {
            Log.Error("Failed to save dropped scene '{0}': {1}", scene.name, exception);
        }
    }

    private static void SavePrefab(EditorContext context, GameObject gameObject)
    {
        try
        {
            string path = context.sceneWorkspace.SavePrefab(gameObject, context.selection.currentDirectory);
            context.selection.SetSelectedPath(path);
        }
        catch (Exception exception)
        {
            Log.Error("Failed to save dropped prefab '{0}': {1}", gameObject.name, exception);
        }
    }
}
