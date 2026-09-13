using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Core.Graphs;
using Inno.Editor.Assets;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Rendering;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Draft = Inno.Editor.Assets.AssetDraftDocuments<Inno.Rendering.MaterialAsset>.Draft;

namespace Inno.Editor.Shaders;

/// <summary>Owns Material authoring sessions; native source editing uses the shared asset draft lifecycle.</summary>
[EditorModule("rendering.material-documents", order: 170)]
public sealed class MaterialDocuments : EditorModule
{
    internal const string C_HISTORY = "inno.material/draft";
    private readonly AssetDraftDocuments<MaterialAsset> m_documents;
    private readonly SerializationRegistry m_serialization;
    private readonly ConditionalWeakTable<ShaderAsset, PresentationCache> m_presentations = new();
    internal readonly AssetPipeline assets;
    internal readonly EditorInteractions interactions;
    internal readonly IEditorPreviewService previews;

    internal MaterialDocuments(AssetPipeline assets, SerializationRegistry serialization, EditorInteractions interactions, IEditorPreviewService previews)
    {
        this.assets = assets; this.interactions = interactions; this.previews = previews;
        m_serialization = serialization;
        m_documents = new(assets, serialization, interactions, "inno.material", C_HISTORY, ".imaterial", "Material");
    }

    /// <summary>Opens native source, including a source with failed import, without publishing its draft.</summary>
    /// <param name="path">Mounted Material source path.</param>
    /// <returns>The persistent identity used for subsequent draft operations.</returns>
    public Guid Open(AssetPath path) => m_documents.Open(path);

    /// <summary>Reads a detached current-generation Material; referenced assets remain canonical read-only inputs.</summary>
    /// <param name="assetId">Open Material source identity.</param>
    /// <returns>The current unsaved Material value.</returns>
    public MaterialAsset Read(Guid assetId) => m_documents.Read(assetId);

    /// <summary>Changes a draft through shared History without modifying its source or Scene/Game.</summary>
    /// <param name="assetId">Open Material source identity.</param>
    /// <param name="candidate">Detached edited Material.</param>
    /// <param name="finishGesture">Whether this sample finishes the active gesture.</param>
    public void Replace(Guid assetId, MaterialAsset candidate, bool finishGesture = true)
        => m_documents.Replace(assetId, candidate, finishGesture);

    /// <summary>Finishes a draft gesture without saving its source.</summary>
    /// <param name="assetId">Open Material source identity.</param>
    public void Commit(Guid assetId) => m_documents.Commit(assetId);

    /// <summary>Changes compatible selected drafts within one shared gesture.</summary>
    /// <param name="candidates">Detached Material values keyed by persistent source identity.</param>
    /// <param name="finishGesture">Whether this sample finishes the shared gesture.</param>
    public void ReplaceMany(IReadOnlyDictionary<Guid, MaterialAsset> candidates, bool finishGesture = true)
        => m_documents.ReplaceMany(candidates, finishGesture);

    /// <summary>Completes a multi-document gesture as one shared History transaction.</summary>
    /// <param name="assetIds">Participating persistent source identities.</param>
    public void CommitMany(IEnumerable<Guid> assetIds) => m_documents.CommitMany(assetIds);

    internal Draft OpenDraft(AssetPath path) => m_documents.GetDraft(Open(path));
    internal Draft Open(MaterialAsset material)
    {
        if (!assets.TryGetInfo(material.identity.persistentId, out AssetInfo? info) || info is null)
            throw new IOException("The Material source identity is unavailable.");
        return OpenDraft(info.assetPath);
    }
    internal void TouchInspection(Draft draft) => m_documents.TouchInspection(draft.id);
    internal ShaderParameterPresentation Presentation(ShaderAsset shader, ShaderPropertyId propertyId, out string error)
    {
        PresentationCache cache = m_presentations.GetValue(shader, static _ => new());
        if (cache.contentVersion != shader.contentVersion)
        {
            cache.contentVersion = shader.contentVersion;
            cache.graph = null;
            cache.error = "";
            try { cache.graph = ShaderGraphArtifact.ReadDocument(ShaderGraphArtifact.Read(shader, assets), m_serialization); }
            catch (Exception failure) when (Recoverable(failure)) { cache.error = failure.Message; }
        }
        error = cache.error;
        return cache.graph is null ? new() : ShaderParameterPresentation.Read(cache.graph, propertyId,
            m_serialization, AssetSerializationContext.Create(assets));
    }
    internal void Edit(Draft draft, MaterialAsset candidate, bool finishGesture) => Replace(draft.id, candidate, finishGesture);
    internal void Commit(Draft draft) => Commit(draft.id);
    internal void ValidateHistory(EditorHistoryChange change, EditorHistoryDirection direction) => m_documents.ValidateHistory(change, direction);
    internal void ApplyHistory(EditorHistoryChange change, EditorHistoryDirection direction) => m_documents.ApplyHistory(change, direction);
    internal static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException
        && Inno.Core.Execution.RetirementPendingException.Find(error) is null;

    /// <inheritdoc />
    protected override void OnStart(EditorContext context) => m_documents.Start();
    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context) => m_documents.Update();
    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        m_presentations.Clear();
        m_documents.Dispose();
    }

    private sealed class PresentationCache
    {
        internal long contentVersion = -1;
        internal GraphDocument? graph;
        internal string error = "";
    }
}

[EditorHistoryHandler(MaterialDocuments.C_HISTORY)]
internal sealed class MaterialDraftHistory(MaterialDocuments documents) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(EditorHistoryContext context, EditorHistoryChange change, EditorHistoryDirection direction)
    {
        try { documents.ValidateHistory(change, direction); return EditorHistoryAvailability.Available(); }
        catch (Exception error) when (MaterialDocuments.Recoverable(error)) { return EditorHistoryAvailability.Unavailable(error.Message); }
    }
    protected override EditorHistoryResult Apply(EditorHistoryContext context, EditorHistoryChange change, EditorHistoryDirection direction)
    {
        try { documents.ApplyHistory(change, direction); return EditorHistoryResult.Success(); }
        catch (Exception error) when (MaterialDocuments.Recoverable(error)) { return EditorHistoryResult.Failure(error.Message); }
    }
}
