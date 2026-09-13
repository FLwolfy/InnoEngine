using System;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Editor.Assets;
using Inno.Editor.Inspection;
using Inno.Rendering;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Rendering;

[InspectionDrawer(typeof(RenderPipelineAsset))]
internal sealed class PipelineInspectionDrawer : InspectionDrawer<RenderPipelineAsset>
{
    public override string icon => Inno.Adapter.Presentation.ImGui.ImGuiIcon.File;
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, RenderPipelineAsset target)
        => (target.name, null);
    protected override void Draw(InspectionDrawContext context, RenderPipelineAsset target)
    {
        if (context.interactions.TryGetModule<PipelineDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.identity.persistentId, out AssetInfo? info) && info is not null)
            PipelineInspector.Draw(context, documents, documents.Open(info.assetPath));
    }
}

[InspectionDrawer(typeof(AssetFileEntry), priority: 100, conditional: true)]
internal sealed class PipelineSourceDrawer : InspectionDrawer<AssetFileEntry>
{
    public override string icon => Inno.Adapter.Presentation.ImGui.ImGuiIcon.File;
    protected override bool CanInspect(AssetFileEntry target)
        => !target.isDirectory && target.extension.Equals(".irenderpipeline", StringComparison.OrdinalIgnoreCase);
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, AssetFileEntry target)
        => (target.nameWithoutExtension, null);
    protected override void Draw(InspectionDrawContext context, AssetFileEntry target)
    {
        if (context.interactions.TryGetModule<PipelineDocuments>(out var documents) && documents is not null)
            PipelineInspector.Draw(context, documents, documents.Open(target.assetPath));
    }
}

internal static class PipelineInspector
{
    internal static void Draw(InspectionDrawContext context, PipelineDocuments documents, Guid id)
    {
        AssetDraftDocuments<RenderPipelineAsset>.Draft draft = documents.drafts.GetDraft(id);
        documents.drafts.TouchInspection(id);
        RenderPipelineAsset pipeline = documents.Read(id);
        var edits = new DraftEdits(documents, id, pipeline);
        UI.PushID(id.ToString("N"));
        try
        {
            Widget.SectionHeader("Render Pipeline", "This source is the configuration authority. Unsaved settings do not change Scene, Game or Player content.");
            UI.BeginDisabled(draft.readOnly);
            try
            {
                if (UI.Button("Save")) context.interactions.documents.Save(draft.documentId);
                UI.SameLine();
                if (UI.Button("Revert")) context.interactions.documents.Revert(draft.documentId);
            }
            finally { UI.EndDisabled(); }
            Widget.Hint(draft.isDirty ? "Unsaved changes · rendering unchanged" : "Saved · import and activation are separate");
            if (draft.error.Length != 0) Widget.Hint(draft.error);
            if (draft.readOnly) Widget.Hint("Installed Pipeline · copy into the project to edit");
            Widget.Hint("Pipeline: " + pipeline.pipelineTypeId);
            DrawSettings("pipeline.settings", pipeline.pipelineState, value => pipeline.pipelineState = value);

            Widget.SectionHeader("Features", "Ordered extension settings belong to this Pipeline. Each enabled feature participates in its render graph.");
            RenderFeatureConfiguration[] features = pipeline.features;
            for (int index = 0; index < features.Length; index++)
            {
                int slot = index;
                UI.PushID(slot);
                try
                {
                    RenderFeatureConfiguration feature = features[slot];
                    context.properties.DrawValue(context.editorContext, draft, "pipeline.features." + slot + ".enabled",
                        feature.featureTypeId, typeof(bool), () => features[slot].enabled,
                        value => { features[slot].enabled = (bool)value!; pipeline.features = features; }, edits, draft.readOnly);
                    DrawSettings("pipeline.features." + slot, feature.state, value =>
                    { features[slot].state = value; pipeline.features = features; });
                    UI.BeginDisabled(draft.readOnly);
                    try
                    {
                        if (slot > 0 && UI.SmallButton("Move Up"))
                        { (features[slot - 1], features[slot]) = (features[slot], features[slot - 1]); pipeline.features = features; documents.Replace(id, pipeline); break; }
                        if (slot + 1 < features.Length)
                        {
                            if (slot > 0) UI.SameLine();
                            if (UI.SmallButton("Move Down"))
                            { (features[slot + 1], features[slot]) = (features[slot], features[slot + 1]); pipeline.features = features; documents.Replace(id, pipeline); break; }
                        }
                    }
                    finally { UI.EndDisabled(); }
                }
                finally { UI.PopID(); }
            }
            if (features.Length == 0) Widget.Hint("No additional features");
            if (!UI.IsAnyItemActive()) documents.Commit(id);
        }
        finally { UI.PopID(); }

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
        public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
        { mutation(); documents.Replace(id, pipeline, !UI.IsAnyItemActive()); return true; }
    }

    private sealed class SettingsEdits(DraftEdits edits, Action capture) : IInspectionPropertyEditService
    {
        public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
            => edits.ChangeProperty(owner, propertyName, () => { mutation(); capture(); }, historyName);
    }
}
