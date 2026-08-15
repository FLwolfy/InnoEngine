using System;
using System.Collections.Generic;
using System.Reflection;

using Inno.Assets;
using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Core.Identity;
using Inno.Editor.ImGui;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Inspection.Drawers;

[PropertyDrawer(typeof(AssetRef<>))]
internal sealed class AssetReferencePropertyDrawer : IPropertyDrawer
{
    private const string C_ASSET_PAYLOAD = "INNO_ASSET";
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private static readonly object C_SYNC = new();
    private static readonly Dictionary<Type, AssetCandidate[]> s_candidatesByType = [];
    private static readonly Dictionary<string, string> s_searchByPath = new(StringComparer.Ordinal);

    static AssetReferencePropertyDrawer()
    {
        AssetManager.SourceFileSystemChanged += static _ =>
        {
            lock (C_SYNC)
            {
                s_candidatesByType.Clear();
            }
        };
    }

    /// <inheritdoc />
    public void Draw(PropertyDrawContext context)
    {
        Type assetType = context.propertyType.GetGenericArguments()[0];
        AssetCandidate[] candidates = GetCandidates(assetType);
        Guid persistentId = ReadPersistentId(context.GetValue());
        AssetCandidate? selected = Array.Find(candidates, candidate => candidate.persistentId == persistentId);
        string preview = persistentId == Guid.Empty
            ? "None"
            : selected is null
                ? $"Missing ({persistentId})"
                : selected.relativePath;

        bool open = NativeImGui.BeginCombo($"##{context.path}", preview);
        if (ImGuiWidget.DragDropTarget<Guid>(C_ASSET_PAYLOAD, out Guid droppedId) &&
            Array.Exists(candidates, candidate => candidate.persistentId == droppedId))
        {
            context.SetValue(CreateReference(assetType, droppedId));
        }

        if (!open)
        {
            return;
        }

        string search = s_searchByPath.TryGetValue(context.path, out string? existingSearch)
            ? existingSearch
            : string.Empty;
        _ = ImGuiWidget.SearchInput(
            context.path,
            "Search assets...",
            ref search,
            C_SEARCH_BUFFER_SIZE);
        s_searchByPath[context.path] = search;

        if (NativeImGui.Selectable("None", persistentId == Guid.Empty))
        {
            context.SetValue(CreateReference(assetType, Guid.Empty));
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            AssetCandidate candidate = candidates[i];
            if (!string.IsNullOrWhiteSpace(search) &&
                candidate.relativePath.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (NativeImGui.Selectable(candidate.relativePath, candidate.persistentId == persistentId))
            {
                context.SetValue(CreateReference(assetType, candidate.persistentId));
            }
        }

        NativeImGui.EndCombo();
    }

    private static AssetCandidate[] GetCandidates(Type targetAssetType)
    {
        lock (C_SYNC)
        {
            if (s_candidatesByType.TryGetValue(targetAssetType, out AssetCandidate[]? cached))
            {
                return cached;
            }

            IReadOnlyList<AssetFileEntry> entries = AssetManager.GetFileSystemEntries(includeDirectories: false);
            var candidates = new List<AssetCandidate>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                AssetFileEntry entry = entries[i];
                if (!AssetManager.TryGetAssetType(entry.relativePath, out Type? assetType) ||
                    assetType is null ||
                    !targetAssetType.IsAssignableFrom(assetType))
                {
                    continue;
                }

                Guid persistentId = AssetManager.GetRef<AssetObject>(entry.relativePath).identity.persistentId;
                if (persistentId != Guid.Empty)
                {
                    candidates.Add(new AssetCandidate(entry.relativePath, persistentId));
                }
            }

            candidates.Sort(static (left, right) =>
                string.Compare(left.relativePath, right.relativePath, StringComparison.OrdinalIgnoreCase));
            cached = candidates.ToArray();
            s_candidatesByType[targetAssetType] = cached;
            return cached;
        }
    }

    private static Guid ReadPersistentId(object? reference)
    {
        object? identity = reference?.GetType().GetProperty("identity")?.GetValue(reference);
        return identity is Identity assetIdentity ? assetIdentity.persistentId : Guid.Empty;
    }

    private static object CreateReference(Type assetType, Guid persistentId)
    {
        MethodInfo method = Array.Find(
            typeof(AssetManager).GetMethods(BindingFlags.Public | BindingFlags.Static),
            candidate =>
                candidate.Name == nameof(AssetManager.GetRef) &&
                candidate.IsGenericMethodDefinition &&
                candidate.GetParameters().Length == 1 &&
                candidate.GetParameters()[0].ParameterType == typeof(Identity))
            ?? throw new MissingMethodException(nameof(AssetManager), nameof(AssetManager.GetRef));
        return method.MakeGenericMethod(assetType).Invoke(null, [new Identity(persistentId)])!;
    }

    private sealed record AssetCandidate(string relativePath, Guid persistentId);
}
