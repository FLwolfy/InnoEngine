using System;
using System.Collections.Generic;

using Inno.Core.Identity;
using Inno.Editor.ImGui;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection.Drawers;

[PropertyDrawer(typeof(GameObject), useForChildren: true, priority: 100)]
[PropertyDrawer(typeof(GameComponent), useForChildren: true, priority: 100)]
internal sealed class EngineObjectReferencePropertyDrawer : IPropertyDrawer
{
    private const string C_SCENE_OBJECT_PAYLOAD = "INNO_SCENE_OBJECT";
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly Dictionary<string, string> m_searchByPath = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Type targetType = context.propertyType;
        EngineObject? selected = context.GetValue() as EngineObject;
        List<EngineObject> candidates = CollectCandidates(context, targetType);
        string preview = selected is null || selected.isDestroyed
            ? "None"
            : GetDisplayName(selected);

        bool open = NativeImGui.BeginCombo($"##{context.path}", preview);
        if (ImGuiWidget.DragDropTarget<Guid>(C_SCENE_OBJECT_PAYLOAD, out Guid droppedId))
        {
            EngineObject? dropped = ResolveDroppedCandidate(
                candidates,
                droppedId,
                targetType,
                context.editorContext.scene);
            if (dropped is not null)
                context.SetValue(dropped);
        }

        if (!open)
            return;

        string search = m_searchByPath.TryGetValue(context.path, out string? currentSearch)
            ? currentSearch
            : string.Empty;
        _ = ImGuiWidget.SearchInput(
            context.path,
            "Search scene objects...",
            ref search,
            C_SEARCH_BUFFER_SIZE);
        m_searchByPath[context.path] = search;

        if (NativeImGui.Selectable("None", selected is null))
            context.SetValue(null);

        for (int i = 0; i < candidates.Count; i++)
        {
            EngineObject candidate = candidates[i];
            string displayName = GetDisplayName(candidate);
            if (!string.IsNullOrWhiteSpace(search) &&
                displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (NativeImGui.Selectable(displayName, ReferenceEquals(candidate, selected)))
                context.SetValue(candidate);
        }

        NativeImGui.EndCombo();
    }

    private static List<EngineObject> CollectCandidates(PropertyDrawContext context, Type targetType)
    {
        IReadOnlyList<GameObject> objects = context.editorContext.scene.GetObjects();
        var candidates = new List<EngineObject>(objects.Count);
        for (int objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            GameObject gameObject = objects[objectIndex];
            if (targetType.IsInstanceOfType(gameObject))
                candidates.Add(gameObject);

            IReadOnlyList<GameComponent> components = gameObject.GetComponents();
            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                if (targetType.IsInstanceOfType(components[componentIndex]))
                    candidates.Add(components[componentIndex]);
            }
        }
        return candidates;
    }

    private static EngineObject? ResolveDroppedCandidate(
        List<EngineObject> candidates,
        Guid persistentId,
        Type targetType,
        GameScene scene)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].identity.persistentId == persistentId)
                return candidates[i];
        }

        GameObject? droppedObject = IdentityManager.Get<GameObject>(persistentId);
        if (droppedObject is null || !droppedObject.isRuntimeValid || !ReferenceEquals(droppedObject.scene, scene))
            return null;
        IReadOnlyList<GameComponent> components = droppedObject.GetComponents();
        for (int i = 0; i < components.Count; i++)
        {
            if (targetType.IsInstanceOfType(components[i]))
                return components[i];
        }
        return null;
    }

    private static string GetDisplayName(EngineObject target)
    {
        return target switch
        {
            GameObject gameObject => gameObject.name,
            GameComponent component => $"{component.gameObject.name} ({component.GetType().Name})",
            _ => target.GetType().Name
        };
    }
}
