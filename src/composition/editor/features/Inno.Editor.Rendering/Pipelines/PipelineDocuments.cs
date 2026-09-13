using System;
using System.IO;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Editor.Assets;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Extensibility.Types;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>Edits Pipeline sources and typed extension settings without changing canonical rendering assets before Save.</summary>
[EditorModule("rendering.pipeline-documents", order: 170)]
public sealed class PipelineDocuments : EditorModule
{
    internal const string C_HISTORY = "inno.pipeline/draft";
    private readonly TypeCatalog m_types;
    internal readonly AssetPipeline assets;
    internal readonly SerializationRegistry serialization;
    internal readonly AssetDraftDocuments<RenderPipelineAsset> drafts;

    internal PipelineDocuments(AssetPipeline assets, SerializationRegistry serialization, TypeCatalog types, EditorInteractions interactions)
    {
        this.assets = assets; this.serialization = serialization; m_types = types;
        drafts = new(assets, serialization, interactions, "inno.pipeline", C_HISTORY, ".irenderpipeline", "Pipeline");
    }

    /// <summary>Opens a native Pipeline source in the shared document service, including failed imports.</summary>
    /// <param name="path">Mounted source path.</param>
    /// <returns>The persistent source identity used by draft operations.</returns>
    public Guid Open(AssetPath path) => drafts.Open(path);

    /// <summary>Reads a detached current-generation Pipeline value; its referenced canonical assets must not be mutated.</summary>
    /// <param name="assetId">Open Pipeline identity.</param>
    /// <returns>The unsaved native Pipeline value.</returns>
    public RenderPipelineAsset Read(Guid assetId) => drafts.Read(assetId);

    /// <summary>Changes a draft through shared History without publishing it to Scene/Game.</summary>
    /// <param name="assetId">Open Pipeline identity.</param>
    /// <param name="candidate">Detached edited value.</param>
    /// <param name="finishGesture">Whether this sample completes the gesture.</param>
    public void Replace(Guid assetId, RenderPipelineAsset candidate, bool finishGesture = true)
        => drafts.Replace(assetId, candidate, finishGesture);

    /// <summary>Completes one gesture without saving the source.</summary>
    /// <param name="assetId">Open Pipeline identity.</param>
    public void Commit(Guid assetId) => drafts.Commit(assetId);

    /// <summary>Captures typed Pipeline settings with this source owner's complete reference and dependency context.</summary>
    /// <typeparam name="TSettings">Current plugin settings contract.</typeparam>
    /// <param name="assetId">Open Pipeline identity.</param>
    /// <param name="settings">Detached edited settings.</param>
    /// <param name="finishGesture">Whether this sample completes the gesture.</param>
    public void ReplaceSettings<TSettings>(Guid assetId, TSettings settings, bool finishGesture = true)
        where TSettings : class, ISerializable
    {
        RenderPipelineAsset pipeline = Read(assetId);
        pipeline.pipelineState = new SerializedRenderExtensionState(assets.CaptureProperties(settings));
        Replace(assetId, pipeline, finishGesture);
    }

    internal ISerializable? RestoreSettings(SerializedRenderExtensionState state)
    {
        if (state.stableTypeId == Guid.Empty) return null;
        Type type = m_types.Resolve(new TypeRef(state.stableTypeId));
        if (Activator.CreateInstance(type) is not ISerializable settings)
            throw new InvalidOperationException("Pipeline settings must be a constructible native serializable type.");
        assets.RestoreProperties(state.stableTypeId, state.propertyData, settings);
        return settings;
    }

    internal static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException
        && Inno.Core.Execution.RetirementPendingException.Find(error) is null;

    /// <inheritdoc />
    protected override void OnStart(EditorContext context) => drafts.Start();
    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context) => drafts.Update();
    /// <inheritdoc />
    protected override void OnStop(EditorContext context) => drafts.Dispose();
}

[EditorHistoryHandler(PipelineDocuments.C_HISTORY)]
internal sealed class PipelineDraftHistory(PipelineDocuments documents) : EditorHistoryHandler
{
    protected override EditorHistoryAvailability Query(EditorHistoryContext context, EditorHistoryChange change, EditorHistoryDirection direction)
    {
        try { documents.drafts.ValidateHistory(change, direction); return EditorHistoryAvailability.Available(); }
        catch (Exception error) when (PipelineDocuments.Recoverable(error)) { return EditorHistoryAvailability.Unavailable(error.Message); }
    }
    protected override EditorHistoryResult Apply(EditorHistoryContext context, EditorHistoryChange change, EditorHistoryDirection direction)
    {
        try { documents.drafts.ApplyHistory(change, direction); return EditorHistoryResult.Success(); }
        catch (Exception error) when (PipelineDocuments.Recoverable(error)) { return EditorHistoryResult.Failure(error.Message); }
    }
}
