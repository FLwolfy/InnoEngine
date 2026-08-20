using Inno.Editor.Panel.Hierarchy;

using System;
using System.Collections.Generic;

using Inno.Core.Identity;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Editor.Panel.Hierarchy.Workspace;
using Inno.Engine.Scene;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Hierarchy;

internal sealed class HierarchySelection(
    EditorSceneWorkspace workspace,
    EditorInteractions interactions)
{
    internal void Prune(EditorContext context)
    {
        if (interactions.selection.TryGet(out GameScene? selectedScene) &&
            (!selectedScene.isLoaded || !ContainsScene(workspace.scenes, selectedScene)))
        {
            _ = interactions.For(HierarchyAreas.Hierarchy).Select();
            return;
        }
        if (interactions.selection.TryGet(out GameObject? gameObject) &&
            (!gameObject.isRuntimeValid || !ContainsScene(workspace.scenes, gameObject.scene)))
            _ = interactions.For(HierarchyAreas.Hierarchy).Select();
    }

    internal bool DeleteObject(EditorContext context, Guid persistentId)
    {
        GameObject? gameObject = IdentityManager.Get<GameObject>(persistentId);
        if (gameObject is null || !gameObject.isRuntimeValid || !ContainsScene(workspace.scenes, gameObject.scene))
            return false;

        _ = gameObject.scene.DestroyObject(gameObject);
        if (interactions.selection.TryGet(out GameObject? selected) && ReferenceEquals(selected, gameObject))
            _ = interactions.For(HierarchyAreas.Hierarchy).Select();
        return true;
    }

    private static bool ContainsScene(IReadOnlyList<GameScene> scenes, GameScene scene)
    {
        for (int i = 0; i < scenes.Count; i++)
        {
            if (ReferenceEquals(scenes[i], scene))
                return true;
        }
        return false;
    }

    internal IReadOnlyList<GameObject> GetRootObjects(GameScene scene)
    {
        IReadOnlyList<GameObject> objects = scene.GetObjects();
        var roots = new List<GameObject>(objects.Count);
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i].transform.parent is null)
                roots.Add(objects[i]);
        }
        roots.Sort(static (left, right) => left.transform.siblingIndex.CompareTo(right.transform.siblingIndex));
        return roots;
    }

    internal void DrawSceneRowContent(EditorContext context, GameScene scene)
    {
        ImGuiWidget.IconText(
            ImGuiIcon.LayerGroup,
            scene.name,
            ReferenceEquals(scene, SceneManager.activeScene));
        if (!workspace.IsDirty(scene))
            return;
        NativeImGui.SameLine(0f, 0f);
        using ImGuiFontScope font = ImGuiFont.PushStyle(ImGuiFontStyle.Italic);
        NativeImGui.TextUnformatted(" *");
    }
}
