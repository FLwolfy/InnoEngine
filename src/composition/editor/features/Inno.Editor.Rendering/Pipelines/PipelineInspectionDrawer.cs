using System;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Editor.Assets;
using Inno.Editor.Inspection;
using Inno.Rendering;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Rendering;

[InspectionDrawer(typeof(RenderPipelineAsset))]
internal sealed class PipelineInspectionDrawer(IInspectionIconProvider<AssetFileEntry> icons) : InspectionDrawer<RenderPipelineAsset>
{
    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
public override string icon => Inno.Adapter.Presentation.ImGui.ImGuiIcon.File;
    /// <summary>
    /// Retrieves the current icon from authoritative state.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
protected override string GetIcon(InspectionDrawContext context, RenderPipelineAsset target)
        => context.interactions.TryGetModule<PipelineDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.identity.persistentId, out AssetInfo? info) && info is not null
            && documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry) ? icons.GetIcon(entry) : icon;
    /// <summary>
    /// Binds a caller-visible label to the current inspection target.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated (string name, actionstring? setter) that represents the completed operation.
    /// </returns>
protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, RenderPipelineAsset target)
        => (target.name, null);
    /// <summary>
    /// Renders the header presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
protected override void DrawHeader(InspectionDrawContext context, RenderPipelineAsset target)
    {
        if (context.interactions.TryGetModule<PipelineDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.identity.persistentId, out AssetInfo? info) && info is not null)
            PipelineInspector.DrawHeader(context, documents, documents.Open(info.assetPath));
    }
    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
protected override void Draw(InspectionDrawContext context, RenderPipelineAsset target)
    {
        if (context.interactions.TryGetModule<PipelineDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.identity.persistentId, out AssetInfo? info) && info is not null)
            PipelineInspector.Draw(context, documents, documents.Open(info.assetPath));
    }
}

[InspectionDrawer(typeof(AssetFileEntry), priority: 100, conditional: true)]
internal sealed class PipelineSourceDrawer(IInspectionIconProvider<AssetFileEntry> icons) : InspectionDrawer<AssetFileEntry>
{
    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
public override string icon => Inno.Adapter.Presentation.ImGui.ImGuiIcon.File;
    /// <summary>
    /// Retrieves the current icon from authoritative state.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated text representation owned by the caller.
    /// </returns>
protected override string GetIcon(InspectionDrawContext context, AssetFileEntry target) => icons.GetIcon(target);
    /// <summary>
    /// Checks whether this drawer supports the selected Inspector target.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
protected override bool CanInspect(AssetFileEntry target)
        => !target.isDirectory && target.extension.Equals(".irenderpipeline", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Binds a caller-visible label to the current inspection target.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// The validated (string name, actionstring? setter) that represents the completed operation.
    /// </returns>
protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, AssetFileEntry target)
        => (target.nameWithoutExtension, null);
    /// <summary>
    /// Renders the header presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
protected override void DrawHeader(InspectionDrawContext context, AssetFileEntry target)
    {
        if (context.interactions.TryGetModule<PipelineDocuments>(out var documents) && documents is not null)
            PipelineInspector.DrawHeader(context, documents, documents.Open(target.assetPath));
    }
    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
protected override void Draw(InspectionDrawContext context, AssetFileEntry target)
    {
        if (context.interactions.TryGetModule<PipelineDocuments>(out var documents) && documents is not null)
            PipelineInspector.Draw(context, documents, documents.Open(target.assetPath));
    }
}

internal static class PipelineInspector
{
    internal static void DrawHeader(InspectionDrawContext context, PipelineDocuments documents, Guid id)
    {
        AssetDraftDocuments<RenderPipelineAsset>.Draft draft = documents.drafts.GetDraft(id);
        ImGuiApi.BeginDisabled(draft.readOnly);
        try
        {
            if (ImGuiApi.Button("Save")) _ = context.interactions.documents.Save(draft.documentId);
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Revert")) _ = context.interactions.documents.Revert(draft.documentId);
        }
        finally { ImGuiApi.EndDisabled(); }
    }

    internal static void Draw(InspectionDrawContext context, PipelineDocuments documents, Guid id)
    {
        AssetDraftDocuments<RenderPipelineAsset>.Draft draft = documents.drafts.GetDraft(id);
        documents.drafts.TouchInspection(id);
        RenderPipelineAsset pipeline = documents.Read(id);
        var edits = new DraftEdits(documents, id, pipeline);
        ImGuiApi.PushID(id.ToString("N"));
        try
        {
            if (Widget.SectionHeader("Render Pipeline", "This source is the configuration authority. Unsaved settings do not change Scene, Game or Player content."))
            {
                Widget.Hint(draft.isDirty ? "Unsaved changes · rendering unchanged" : "Saved · import and activation are separate");
                if (draft.error.Length != 0) Widget.Hint(draft.error);
                if (draft.readOnly) Widget.Hint("Installed Pipeline · copy into the project to edit");
                Widget.Hint("Pipeline: " + pipeline.pipelineTypeId);
                DrawSettings("pipeline.settings", pipeline.pipelineState, value => pipeline.pipelineState = value);
            }

            bool featuresOpen = Widget.SectionHeader("Features", "Ordered extension settings belong to this Pipeline. Each enabled feature participates in its render graph.");
            RenderFeatureConfiguration[] features = pipeline.features;
            for (int index = 0; featuresOpen && index < features.Length; index++)
            {
                int slot = index;
                ImGuiApi.PushID(slot);
                try
                {
                    RenderFeatureConfiguration feature = features[slot];
                    context.properties.DrawValue(context.editorContext, draft, "pipeline.features." + slot + ".enabled",
                        feature.featureTypeId, typeof(bool), () => features[slot].enabled,
                        value => { features[slot].enabled = (bool)value!; pipeline.features = features; }, edits, draft.readOnly);
                    DrawSettings("pipeline.features." + slot, feature.state, value =>
                    { features[slot].state = value; pipeline.features = features; });
                    ImGuiApi.BeginDisabled(draft.readOnly);
                    try
                    {
                        if (slot > 0 && ImGuiApi.SmallButton("Move Up"))
                        { (features[slot - 1], features[slot]) = (features[slot], features[slot - 1]); pipeline.features = features; documents.Replace(id, pipeline); break; }
                        if (slot + 1 < features.Length)
                        {
                            if (slot > 0) ImGuiApi.SameLine();
                            if (ImGuiApi.SmallButton("Move Down"))
                            { (features[slot + 1], features[slot]) = (features[slot], features[slot + 1]); pipeline.features = features; documents.Replace(id, pipeline); break; }
                        }
                    }
                    finally { ImGuiApi.EndDisabled(); }
                }
                finally { ImGuiApi.PopID(); }
            }
            if (featuresOpen && features.Length == 0) Widget.Hint("No additional features");
            if (!ImGuiApi.IsAnyItemActive()) documents.Commit(id);
        }
        finally { ImGuiApi.PopID(); }

        void DrawSettings(string path, SerializedRenderExtensionState state, Action<SerializedRenderExtensionState> assign)
        {
            try
            {
                ISerializable? settings = documents.RestoreSettings(state);
                if (settings is null) { Widget.Hint("No typed settings"); return; }
                var settingsEdits = new SettingsEdits(edits, () => assign(new(documents.assets.CaptureProperties(settings))));
                foreach (SerializedProperty property in documents.serialization.GetProperties(settings))
                    context.properties.DrawDraftProperty(context.editorContext, draft, settings, path, property, settingsEdits, draft.readOnly);
            }
            catch (Exception error) when (PipelineDocuments.Recoverable(error))
            { Widget.Hint("Settings unavailable · " + state.stableTypeId + " · " + error.Message + " · Stored properties and dependencies are retained."); }
        }
    }

    private sealed class DraftEdits(PipelineDocuments documents, Guid id, RenderPipelineAsset pipeline) : IInspectionPropertyEditService
    {
        /// <summary>
        /// Applies one serialized property edit and records its reversible history payload.
        /// </summary>
        /// <param name="owner">
        /// The object that owns the resulting lifetime and state.
        /// </param>
        /// <param name="propertyName">
        /// The property name text validated by the change property operation.
        /// </param>
        /// <param name="mutation">
        /// The callback invoked by change property within the operation's owned lifetime.
        /// </param>
        /// <param name="historyName">
        /// The history name text validated by the change property operation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
        { mutation(); documents.Replace(id, pipeline, !ImGuiApi.IsAnyItemActive()); return true; }
    }

    private sealed class SettingsEdits(DraftEdits edits, Action capture) : IInspectionPropertyEditService
    {
        /// <summary>
        /// Applies one serialized property edit and records its reversible history payload.
        /// </summary>
        /// <param name="owner">
        /// The object that owns the resulting lifetime and state.
        /// </param>
        /// <param name="propertyName">
        /// The property name text validated by the change property operation.
        /// </param>
        /// <param name="mutation">
        /// The callback invoked by change property within the operation's owned lifetime.
        /// </param>
        /// <param name="historyName">
        /// The history name text validated by the change property operation.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
        /// </returns>
public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
            => edits.ChangeProperty(owner, propertyName, () => { mutation(); capture(); }, historyName);
    }
}
