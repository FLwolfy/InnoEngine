using System;
using System.Linq;
using Inno.Assets.Pipeline;
using Inno.Editor.Inspection;
using Inno.Rendering;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Shaders;

[InspectionDrawer(typeof(MaterialAsset))]
internal sealed class MaterialInspectionDrawer : InspectionDrawer<MaterialAsset>
{
    public override string icon => Inno.Adapter.Presentation.ImGui.ImGuiIcon.File;
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, MaterialAsset target)
        => (target.name, null);
    protected override void Draw(InspectionDrawContext context, MaterialAsset target)
    {
        if (context.interactions.TryGetModule<MaterialDocuments>(out var documents) && documents is not null)
            MaterialInspector.Draw(context, documents, documents.Open(target));
    }
}

[InspectionDrawer(typeof(AssetFileEntry), priority: 100, conditional: true)]
internal sealed class MaterialSourceDrawer : InspectionDrawer<AssetFileEntry>
{
    public override string icon => Inno.Adapter.Presentation.ImGui.ImGuiIcon.File;
    protected override bool CanInspect(AssetFileEntry target)
        => !target.isDirectory && target.extension.Equals(".imaterial", StringComparison.OrdinalIgnoreCase);
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, AssetFileEntry target)
        => (target.nameWithoutExtension, null);
    protected override void Draw(InspectionDrawContext context, AssetFileEntry target)
    {
        if (context.interactions.TryGetModule<MaterialDocuments>(out var documents) && documents is not null)
            MaterialInspector.Draw(context, documents, documents.OpenDraft(target.assetPath));
    }
}

internal static class MaterialInspector
{
    internal static void Draw(InspectionDrawContext context, MaterialDocuments documents, Inno.Editor.Assets.AssetDraftDocuments<MaterialAsset>.Draft draft)
    {
        documents.TouchInspection(draft);
        MaterialAsset material = documents.Read(draft.id);
        var edits = new DraftEdits(documents, draft, material);
        NativeImGui.PushID(draft.id.ToString("N"));
        try
        {
            EditorWidget.SectionHeader("Material", "Edits stay in this draft. Save publishes through normal asset import; Revert reloads the source.");
            EditorWidget.Disabled(draft.readOnly, () =>
            {
                if (NativeImGui.Button("Save")) documents.interactions.documents.Save(draft.documentId);
                NativeImGui.SameLine();
                if (NativeImGui.Button("Revert")) documents.interactions.documents.Revert(draft.documentId);
            });
            EditorWidget.Hint(draft.isDirty ? "Unsaved changes · Scene/Game unchanged" : "Saved · compilation and import are separate");
            if (draft.error.Length != 0) EditorWidget.Hint(draft.error);
            if (draft.readOnly) EditorWidget.Hint("Installed material · copy to the project to edit");
            Draw("shader", "Shader", typeof(ShaderAsset), () => material.shader, value => material.shader = (ShaderAsset?)value);
            ShaderDefinition? definition = material.shader?.definition;
            if (material.shader is null || material.shader.isMissing || definition is null)
            { EditorWidget.Hint("Choose an available Shader. Existing overrides are retained until its interface can be resolved."); return; }
            if (NativeImGui.SmallButton("Open Shader Editor"))
            {
                context.interactions.SetSelection(material.shader);
                context.interactions.OpenPanel("rendering.shader-editor");
            }
            NativeImGui.SameLine();
            if (NativeImGui.SmallButton("Locate Shader")
                && documents.assets.TryGetFileSystemEntry(material.shader.assetPath, out AssetFileEntry shaderEntry))
                context.interactions.SetSelection(shaderEntry);

            if (NativeImGui.CollapsingHeader("Material Preview")
                && context.interactions.TryGetModule<ShaderPreviews>(out var shaderPreviews) && shaderPreviews is not null)
                shaderPreviews.DrawMaterial(draft.id, material, MathF.Max(1f, MathF.Min(256f, NativeImGui.GetContentRegionAvail().X)));

            if (definition.techniques.Length > 1 || material.techniqueId.isValid)
            {
                if (EditorWidget.BeginBoundedCombo("##technique", material.techniqueId.isValid ? material.techniqueId.value : "Automatic"))
                {
                    try
                    {
                        if (NativeImGui.Selectable("Automatic", !material.techniqueId.isValid) && !draft.readOnly)
                        { material.techniqueId = default; documents.Edit(draft, material, true); }
                        foreach (ShaderTechniqueDefinition technique in definition.techniques)
                            if (NativeImGui.Selectable(technique.id.value, material.techniqueId == technique.id) && !draft.readOnly)
                            { material.techniqueId = technique.id; documents.Edit(draft, material, true); }
                    }
                    finally { NativeImGui.EndCombo(); }
                }
            }

            EditorWidget.SectionHeader("Parameters", "Inherited values follow Shader defaults. Changing a field creates a Material override; Reset restores inheritance.");
            string currentGroup = "";
            foreach (ShaderPropertyDefinition property in definition.properties.Where(IsEditable))
            {
                ShaderParameterPresentation presentation = documents.Presentation(material.shader, property.id, out string presentationError);
                if (presentationError.Length != 0) EditorWidget.Hint("Parameter presentation unavailable: " + presentationError);
                if (!presentation.visible) continue;
                if (presentation.group != currentGroup)
                {
                    currentGroup = presentation.group;
                    EditorWidget.SectionHeader(currentGroup.Length == 0 ? "Parameters" : currentGroup);
                }
                bool overridden = material.TryGet(property.id, out MaterialValue value);
                if (overridden && !ShaderPropertyInspector.Compatible(property.type, value.kind)) continue;
                if (!overridden) value = property.defaultValue;
                NativeImGui.PushID(property.id.value);
                try
                {
                    ShaderPropertyInspector.Draw(context, draft, "material." + property.id.value, property, value,
                        next => material.Set(property.id, next), edits, draft.readOnly, presentation);
                    EditorWidget.Hint(overridden ? "Material override" : "Inherited from Shader");
                    if (overridden && !draft.readOnly && NativeImGui.SmallButton("Reset override"))
                    { material.ReplaceProperties(material.properties.Where(entry => entry.id != property.id).ToArray()); documents.Edit(draft, material, true); }
                    if (value.kind == MaterialValueKind.Texture)
                    {
                        if (value.texture is { isMissing: false } texture && documents.previews.TryGetTexture(texture, out var preview))
                        {
                            float width = MathF.Max(1f, MathF.Min(128f, NativeImGui.GetContentRegionAvail().X));
                            float height = width * preview.pixelHeight / preview.pixelWidth;
                            if (height > 128f) { width *= 128f / height; height = 128f; }
                            documents.previews.Draw(preview, new(width, height));
                        }
                    }
                }
                finally { NativeImGui.PopID(); }
            }

            if (material.properties.Count != 0 && !draft.readOnly && NativeImGui.Button("Reset all overrides"))
            { material.ReplaceProperties([]); documents.Edit(draft, material, true); }

            foreach (ShaderKeywordDefinition keyword in definition.keywords)
            {
                string selected = keyword.options.FirstOrDefault(option => material.keywords.Contains(option)) ?? "None";
                if (!EditorWidget.BeginBoundedCombo(keyword.id, selected)) continue;
                try
                {
                    if (NativeImGui.Selectable("None", selected == "None")) SetOption(null);
                    foreach (string option in keyword.options) if (NativeImGui.Selectable(option, selected == option)) SetOption(option);
                }
                finally { NativeImGui.EndCombo(); }
                void SetOption(string? next)
                {
                    if (draft.readOnly) return;
                    foreach (string option in keyword.options) material.SetKeyword(option, option == next);
                    documents.Edit(draft, material, true);
                }
            }

            MaterialPropertyEntry[] orphaned = material.properties.Where(entry => !definition.properties.Any(property => property.id == entry.id
                && IsEditable(property) && ShaderPropertyInspector.Compatible(property.type, entry.value.kind))).ToArray();
            if (orphaned.Length != 0)
            {
                EditorWidget.SectionHeader("Unresolved Overrides", "These values were retained after an interface change. Remove them explicitly or restore a compatible Shader before publishing.");
                foreach (MaterialPropertyEntry entry in orphaned)
                {
                    NativeImGui.TextUnformatted(entry.id.value + " · " + entry.value.kind);
                    if (!draft.readOnly && NativeImGui.SmallButton("Remove##" + entry.id.value))
                    { material.ReplaceProperties(material.properties.Where(value => value.id != entry.id).ToArray()); documents.Edit(draft, material, true); }
                }
            }
            ShaderPropertyDefinition[] passBindings = definition.properties.Where(property => !IsEditable(property)).ToArray();
            string[] unknownKeywords = material.keywords.Where(value => !definition.keywords.Any(keyword => keyword.options.Contains(value))).ToArray();
            if (unknownKeywords.Length != 0)
            {
                EditorWidget.SectionHeader("Unresolved Keywords", "These overrides no longer belong to the Shader interface. They remain until explicitly removed.");
                foreach (string keyword in unknownKeywords)
                {
                    NativeImGui.TextUnformatted(keyword);
                    if (!draft.readOnly && NativeImGui.SmallButton("Remove##keyword." + keyword))
                    { material.SetKeyword(keyword, false); documents.Edit(draft, material, true); }
                }
            }
            if (passBindings.Length != 0)
            {
                EditorWidget.SectionHeader("Pipeline Bindings", "These resources are supplied by the Render Pass, not by this Material.");
                foreach (ShaderPropertyDefinition property in passBindings)
                    EditorWidget.Hint(property.displayName + " · " + property.type + " · " + property.bindingOwner);
            }
            if (!NativeImGui.IsAnyItemActive()) documents.Commit(draft);
        }
        finally { NativeImGui.PopID(); }

        void Draw(string path, string label, Type type, Func<object?> getter, Action<object?> setter)
            => context.properties.DrawValue(context.editorContext, draft, "material." + path, label, type, getter, setter, edits, draft.readOnly);
    }

    private static bool IsEditable(ShaderPropertyDefinition property) => property.bindingOwner == ShaderPropertyBindingOwner.Material
        && property.bindingKind is ShaderPropertyBindingKind.Uniform or ShaderPropertyBindingKind.SampledTexture;

    private sealed class DraftEdits(MaterialDocuments documents, Inno.Editor.Assets.AssetDraftDocuments<MaterialAsset>.Draft draft, MaterialAsset material) : IInspectionPropertyEditService
    {
        public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
        { mutation(); documents.Replace(draft.id, material, !NativeImGui.IsAnyItemActive()); return true; }
    }
}
