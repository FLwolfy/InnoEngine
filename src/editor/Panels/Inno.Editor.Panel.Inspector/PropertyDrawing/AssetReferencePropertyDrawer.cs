
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Numerics;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Core;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[PropertyDrawer(typeof(AssetObject), useForChildren: true, priority: 100)]
internal sealed class AssetReferencePropertyDrawer : IPropertyDrawer, IDisposable
{
    private const nuint C_SEARCH_BUFFER_SIZE = 256;

    private readonly object m_sync = new();
    private readonly AssetPipeline m_assets;
    private ConditionalWeakTable<Type, CandidatesBox> m_candidatesByType = new();
    private readonly Dictionary<string, string> m_searchByPath = new(StringComparer.Ordinal);

    internal AssetReferencePropertyDrawer(AssetPipeline assets)
    {
        m_assets = assets ?? throw new ArgumentNullException(nameof(assets));
        m_assets.Changed += OnAssetsChanged;
    }

    /// <summary>
    /// Unsubscribes from the owning asset pipeline and releases generation-bound candidate caches.
    /// </summary>
    public void Dispose()
    {
        m_assets.Changed -= OnAssetsChanged;
        lock (m_sync)
        {
            m_candidatesByType = new ConditionalWeakTable<Type, CandidatesBox>();
            m_searchByPath.Clear();
        }
    }

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    public void Draw(PropertyDrawContext context)
    {
        Type assetType = context.propertyType;
        AssetCandidate[] candidates = GetCandidates(assetType);
        object? currentValue = context.GetValue();
        Guid persistentId = ReadPersistentId(currentValue);
        AssetCandidate? selected = Array.Find(candidates, candidate => candidate.persistentId == persistentId);
        string preview = persistentId == Guid.Empty
            ? "None"
            : currentValue is AssetObject { isMissing: true } missing
                ? $"Missing {missing.GetType().Name} [{persistentId}]"
            : selected is null
                ? $"Missing ({persistentId})"
                : selected.displayName;

        bool open = EditorWidget.BeginBoundedCombo($"##{context.path}", preview);
        Vector2 dropMinimum = NativeImGui.GetItemRectMin();
        Vector2 dropMaximum = NativeImGui.GetItemRectMax();
        EditorDropWidgetResult drop = EditorDragDropRenderer.Target(
            context.interactions.For(
                InspectorInteractionIds.C_ASSET_REFERENCE_AREA,
                new AssetReferenceDropTarget(
                    assetType,
                    persistentIdToAssign =>
                    {
                        AssetCandidate? dropped = Array.Find(
                            candidates,
                            candidate => candidate.persistentId == persistentIdToAssign);
                        if (dropped is not null)
                            AssignAsset(context, assetType, dropped);
                    })));
        if (drop.isPreviewing && drop.status.canDrop)
            EditorWidget.DropTargetHighlight(dropMinimum, dropMaximum);

        if (!open)
            return;
        try
        {
            string search = m_searchByPath.TryGetValue(context.path, out string? existingSearch)
                ? existingSearch
                : string.Empty;
            _ = EditorWidget.SearchInput(
                context.path,
                "Search assets...",
                ref search,
                C_SEARCH_BUFFER_SIZE);
            m_searchByPath[context.path] = search;

            if (NativeImGui.Selectable("None", persistentId == Guid.Empty))
                context.SetValue(null);

            for (int i = 0; i < candidates.Length; i++)
            {
                AssetCandidate candidate = candidates[i];
                if (!string.IsNullOrWhiteSpace(search) &&
                    candidate.displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    candidate.fullPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (NativeImGui.Selectable(
                        $"{candidate.displayName}##{candidate.persistentId:N}",
                        candidate.persistentId == persistentId))
                {
                    AssignAsset(context, assetType, candidate);
                }
                if (NativeImGui.IsItemHovered() && EditorWidget.BeginMenuTooltip())
                {
                    NativeImGui.TextUnformatted(candidate.fullPath);
                    EditorWidget.EndMenuTooltip();
                }
            }
        }
        finally
        {
            NativeImGui.EndCombo();
        }
    }

    private AssetCandidate[] GetCandidates(Type targetAssetType)
    {
        lock (m_sync)
        {
            if (m_candidatesByType.TryGetValue(targetAssetType, out CandidatesBox? cached))
                return cached.candidates;

            IReadOnlyList<AssetFileEntry> entries = m_assets.GetFileSystemEntries(includeDirectories: false);
            var candidates = new List<AssetCandidate>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                AssetFileEntry entry = entries[i];
                if (!m_assets.TryGetAssetType(entry.assetPath, out Type? assetType) ||
                    assetType is null ||
                    !targetAssetType.IsAssignableFrom(assetType))
                {
                    continue;
                }

                if (m_assets.TryGetPersistentId(entry.assetPath, out Guid persistentId))
                {
                    string name = Path.GetFileNameWithoutExtension(entry.assetPath.localPath);
                    string displayName = $"{entry.assetPath.source.value}:{name}";
                    candidates.Add(new AssetCandidate(
                        entry.assetPath,
                        persistentId,
                        displayName,
                        entry.assetPath.ToString()));
                }
            }

            candidates.Sort(static (left, right) =>
                string.Compare(
                    left.displayName,
                    right.displayName,
                    StringComparison.OrdinalIgnoreCase));
            AssetCandidate[] result = candidates.ToArray();
            m_candidatesByType.Add(targetAssetType, new CandidatesBox(result));
            return result;
        }
    }

    private static Guid ReadPersistentId(object? reference)
        => reference is AssetObject asset ? asset.identity.persistentId : Guid.Empty;

    private void AssignAsset(
        PropertyDrawContext context,
        Type assetType,
        AssetCandidate candidate)
    {
        if (ReadPersistentId(context.GetValue()) == candidate.persistentId)
            return;
        object asset = m_assets.Load(candidate.assetPath, assetType);
        context.SetValue(asset);
    }

    private void OnAssetsChanged(AssetChangeSet changes)
    {
        lock (m_sync)
            m_candidatesByType = new ConditionalWeakTable<Type, CandidatesBox>();
    }

    private sealed record AssetCandidate(
        AssetPath assetPath,
        Guid persistentId,
        string displayName,
        string fullPath);
    private sealed record CandidatesBox(AssetCandidate[] candidates);
}
