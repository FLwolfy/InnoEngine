using System;
using System.Collections.Generic;

using Inno.Core.ECS;
using Inno.Core.Identity;
using Inno.Editor.ImGui;
using Inno.Engine.Scene;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection.Drawers;

[PropertyDrawer(typeof(SceneObjectRef<>))]
internal sealed class SceneObjectReferencePropertyDrawer : IPropertyDrawer
{
    private const string C_SCENE_OBJECT_PAYLOAD = "INNO_SCENE_OBJECT";
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly Dictionary<string, string> m_searchByPath = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Type targetType = context.propertyType.GetGenericArguments()[0];
        Guid persistentId = ReadPersistentId(context.GetValue());
        List<IIdentityObject> candidates = CollectCandidates(context, targetType);
        IIdentityObject? resolved = candidates.Find(candidate => candidate.GetIdentity().persistentId == persistentId);
        string preview = persistentId == Guid.Empty
            ? "None"
            : resolved is null
                ? $"Missing ({persistentId})"
                : GetDisplayName(resolved);

        bool open = NativeImGui.BeginCombo($"##{context.path}", preview);
        if (ImGuiWidget.DragDropTarget<Guid>(C_SCENE_OBJECT_PAYLOAD, out Guid droppedId))
        {
            IIdentityObject? dropped = ResolveDroppedCandidate(context, targetType, droppedId, candidates);
            if (dropped is not null)
            {
                context.SetValue(CreateReference(context.propertyType, dropped.GetIdentity().persistentId));
            }
        }

        if (!open)
        {
            return;
        }

        string search = m_searchByPath.TryGetValue(context.path, out string? existingSearch)
            ? existingSearch
            : string.Empty;
        _ = ImGuiWidget.SearchInput(
            context.path,
            "Search scene objects...",
            ref search,
            C_SEARCH_BUFFER_SIZE);
        m_searchByPath[context.path] = search;

        if (NativeImGui.Selectable("None", persistentId == Guid.Empty))
        {
            context.SetValue(CreateReference(context.propertyType, Guid.Empty));
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            IIdentityObject candidate = candidates[i];
            string displayName = GetDisplayName(candidate);
            if (!string.IsNullOrWhiteSpace(search) &&
                displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Guid candidateId = candidate.GetIdentity().persistentId;
            if (NativeImGui.Selectable(displayName, candidateId == persistentId))
            {
                context.SetValue(CreateReference(context.propertyType, candidateId));
            }
        }

        NativeImGui.EndCombo();
    }

    private static List<IIdentityObject> CollectCandidates(PropertyDrawContext context, Type targetType)
    {
        var candidates = new List<IIdentityObject>();
        foreach (GameObject gameObject in context.editorContext.scene.GetObjects())
        {
            if (targetType.IsInstanceOfType(gameObject))
            {
                candidates.Add(gameObject);
            }

            IReadOnlyList<Component> components = gameObject.GetComponents();
            for (int i = 0; i < components.Count; i++)
            {
                if (targetType.IsInstanceOfType(components[i]) && components[i] is IIdentityObject identityObject)
                {
                    candidates.Add(identityObject);
                }
            }
        }

        return candidates;
    }

    private static IIdentityObject? ResolveDroppedCandidate(
        PropertyDrawContext context,
        Type targetType,
        Guid droppedId,
        List<IIdentityObject> candidates)
    {
        IIdentityObject? exact = candidates.Find(candidate => candidate.GetIdentity().persistentId == droppedId);
        if (exact is not null)
        {
            return exact;
        }

        GameObject? gameObject = IdentityManager.Get<GameObject>(droppedId);
        if (gameObject is null || !ReferenceEquals(gameObject.scene, context.editorContext.scene))
        {
            return null;
        }

        IReadOnlyList<Component> components = gameObject.GetComponents();
        for (int i = 0; i < components.Count; i++)
        {
            if (targetType.IsInstanceOfType(components[i]) && components[i] is IIdentityObject identityObject)
            {
                return identityObject;
            }
        }

        return null;
    }

    private static string GetDisplayName(IIdentityObject target)
    {
        return target switch
        {
            GameObject gameObject => gameObject.name,
            GameBehavior behavior => $"{behavior.gameObject?.name ?? "Missing"} ({behavior.GetType().Name})",
            _ => target.GetType().Name
        };
    }

    private static Guid ReadPersistentId(object? reference)
    {
        return reference?.GetType().GetProperty("persistentId")?.GetValue(reference) is Guid id
            ? id
            : Guid.Empty;
    }

    private static object CreateReference(Type referenceType, Guid persistentId)
    {
        return Activator.CreateInstance(referenceType, [persistentId])
            ?? throw new InvalidOperationException($"Could not create reference '{referenceType.FullName}'.");
    }
}
