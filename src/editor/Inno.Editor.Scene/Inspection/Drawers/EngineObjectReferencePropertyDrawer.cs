using Inno.Editor.Scene.DragDrop;

using Inno.Editor.Scene;

using System;
using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.Core.DragDrop;
using Inno.Editor.Scene.Inspection;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Renderers;
using Inno.Editor.ImGui.Widgets;
using Inno.Editor.Scene.Workspace;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scene.Inspection.Drawers;

[PropertyDrawer(typeof(GameObject), useForChildren: true, priority: 100)]
[PropertyDrawer(typeof(GameComponent), useForChildren: true, priority: 100)]
internal sealed class EngineObjectReferencePropertyDrawer : IPropertyDrawer
{
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly Dictionary<string, string> m_searchByPath = new(StringComparer.Ordinal);
    private readonly EditorSceneWorkspace m_workspace;

    internal EngineObjectReferencePropertyDrawer(EditorSceneWorkspace workspace)
    {
        m_workspace = workspace;
    }

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Type targetType = context.propertyType;
        EngineObject? selected = context.GetValue() as EngineObject;
        List<EngineObject> candidates = CollectCandidates(targetType);
        string preview = selected is null || selected.isDestroyed
            ? "None"
            : GetDisplayName(selected);

        bool open = NativeImGui.BeginCombo($"##{context.path}", preview);
        _ = EditorDragDropRenderer.Target(
            context.editorContext,
            typeof(SceneSurface.EngineObjectReference),
            new EngineObjectReferenceDropTarget(targetType, context.SetValue));

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

    private List<EngineObject> CollectCandidates(Type targetType)
    {
        IReadOnlyList<GameObject> objects = m_workspace.activeScene.GetObjects();
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
