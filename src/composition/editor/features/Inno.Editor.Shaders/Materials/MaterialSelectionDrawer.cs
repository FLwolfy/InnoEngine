using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Inspection;
using Inno.Rendering;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Shaders;

[InspectionDrawer(typeof(AssetInspectionSelection))]
internal sealed class MaterialSelectionDrawer : InspectionDrawer<AssetInspectionSelection>
{
    public override string icon => Inno.Adapter.Presentation.ImGui.ImGuiIcon.File;
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, AssetInspectionSelection target)
        => (target.assetIds.Count + " Assets", null);

    protected override void Draw(InspectionDrawContext context, AssetInspectionSelection target)
    {
        if (!context.interactions.TryGetModule<MaterialDocuments>(out var documents) || documents is null) return;
        var drafts = new List<Inno.Editor.Assets.AssetDraftDocuments<MaterialAsset>.Draft>();
        foreach (Guid id in target.assetIds)
        {
            if (!documents.assets.TryGetInfo(id, out AssetInfo? info) || info is null
                || !documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry)
                || !entry.extension.Equals(".imaterial", StringComparison.OrdinalIgnoreCase))
            { Widget.Hint("Select Materials with compatible Shader interfaces to edit their common parameters."); return; }
            Inno.Editor.Assets.AssetDraftDocuments<MaterialAsset>.Draft draft = documents.OpenDraft(info.assetPath);
            documents.TouchInspection(draft);
            drafts.Add(draft);
        }
        var materials = drafts.ToDictionary(value => value.id, value => documents.Read(value.id));
        bool readOnly = drafts.Any(value => value.readOnly);
        UI.PushID("materials." + string.Join(".", target.assetIds));
        UI.BeginDisabled(readOnly);
        try
        {
            if (UI.Button("Save Selected"))
            {
                documents.CommitMany(target.assetIds);
                foreach (var draft in drafts) context.interactions.documents.Save(draft.documentId);
            }
            UI.SameLine();
            if (UI.Button("Revert Selected"))
            {
                documents.CommitMany(target.assetIds);
                using var transaction = context.interactions.history.BeginTransaction("Revert Materials");
                foreach (var draft in drafts) context.interactions.documents.Revert(draft.documentId);
                transaction.Commit();
            }
            Widget.Hint("Save runs per document. Unsaved edits do not change Scene/Game.");
            if (readOnly) Widget.Hint("Selection includes installed read-only content. Copy it to the project before editing.");
            foreach (var draft in drafts.Where(value => value.error.Length != 0))
                Widget.Hint(draft.path.localPath + ": " + draft.error);
            var edits = new SelectionEdits(documents, materials);
            MaterialAsset first = materials.Values.First();
            Widget.SectionHeader("Material Selection", "Changes are recorded together. Changing Shader retains each Material's independent overrides.");
            if (materials.Values.Any(value => value.shader?.identity.persistentId != first.shader?.identity.persistentId))
                Widget.Hint("Shader · Mixed values");
            context.properties.DrawValue(context.editorContext, target, "materials.shader", "Shader", typeof(ShaderAsset),
                () => first.shader, value =>
                {
                    foreach (MaterialAsset material in materials.Values) material.shader = (ShaderAsset?)value;
                }, edits, readOnly);
            if (first.shader is null || first.shader.isMissing || first.shader.definition is not { } firstDefinition)
            { Widget.Hint("A selected Shader is unavailable."); return; }
            ShaderTechniqueId[] commonTechniques = firstDefinition.techniques.Select(value => value.id)
                .Where(id => materials.Values.All(material => material.shader is { isMissing: false, definition: { } definition }
                    && definition.techniques.Any(technique => technique.id == id))).ToArray();
            bool mixedTechnique = materials.Values.Any(value => value.techniqueId != first.techniqueId);
            if (commonTechniques.Length > 1 || materials.Values.Any(value => value.techniqueId.isValid))
            {
                string current = mixedTechnique ? "Mixed Techniques" : first.techniqueId.isValid ? first.techniqueId.value : "Automatic";
                if (Widget.BeginBoundedCombo("##materials.technique", current))
                {
                    try
                    {
                        if (UI.Selectable("Automatic", !mixedTechnique && !first.techniqueId.isValid)) SetTechnique(default);
                        foreach (ShaderTechniqueId technique in commonTechniques)
                            if (UI.Selectable(technique.value, !mixedTechnique && first.techniqueId == technique)) SetTechnique(technique);
                    }
                    finally { UI.EndCombo(); }
                }
            }
            foreach (ShaderKeywordDefinition keyword in firstDefinition.keywords)
            {
                if (!materials.Values.All(material => material.shader is { isMissing: false, definition: { } definition }
                    && definition.keywords.Any(value => value.id == keyword.id && value.options.SequenceEqual(keyword.options))))
                    continue;
                string Option(MaterialAsset material) => keyword.options.FirstOrDefault(material.keywords.Contains) ?? "None";
                string selected = Option(first);
                bool mixed = materials.Values.Any(material => Option(material) != selected);
                if (!Widget.BeginBoundedCombo(keyword.id, mixed ? "Mixed values" : selected)) continue;
                try
                {
                    if (UI.Selectable("None", !mixed && selected == "None")) SetOption(null);
                    foreach (string option in keyword.options)
                        if (UI.Selectable(option, !mixed && selected == option)) SetOption(option);
                }
                finally { UI.EndCombo(); }
                void SetOption(string? next)
                {
                    foreach (MaterialAsset material in materials.Values)
                        foreach (string option in keyword.options) material.SetKeyword(option, option == next);
                    documents.ReplaceMany(materials);
                }
            }
            Widget.SectionHeader("Common Parameters", "Only compatible Material-owned inputs appear here. Unrelated overrides are retained per Material.");
            string currentGroup = "";
            foreach (ShaderPropertyDefinition property in firstDefinition.properties)
            {
                if (property.bindingOwner != ShaderPropertyBindingOwner.Material
                    || property.bindingKind is not (ShaderPropertyBindingKind.Uniform or ShaderPropertyBindingKind.SampledTexture)) continue;
                var values = new List<MaterialValue>();
                ShaderParameterPresentation presentation = documents.Presentation(first.shader, property.id, out string presentationError);
                if (presentationError.Length != 0) Widget.Hint("Parameter presentation unavailable: " + presentationError);
                if (!presentation.visible) continue;
                bool compatible = true;
                foreach (MaterialAsset material in materials.Values)
                {
                    ShaderPropertyDefinition[] declarations = material.shader is { isMissing: false } shader ? shader.definition?.properties ?? [] : [];
                    int index = Array.FindIndex(declarations, value => value.id == property.id && value.type == property.type
                        && value.bindingKind == property.bindingKind && value.bindingOwner == property.bindingOwner);
                    if (index < 0) { compatible = false; break; }
                    ShaderParameterPresentation other = documents.Presentation(material.shader!, property.id, out _);
                    if (!other.visible) { compatible = false; break; }
                    if (other.hasRange)
                    {
                        if (presentation.hasRange)
                        {
                            presentation.minimum = Math.Max(presentation.minimum, other.minimum);
                            presentation.maximum = Math.Min(presentation.maximum, other.maximum);
                            if (presentation.minimum > presentation.maximum) { compatible = false; break; }
                        }
                        else { presentation.hasRange = true; presentation.minimum = other.minimum; presentation.maximum = other.maximum; }
                    }
                    MaterialValue value = material.TryGet(property.id, out var overridden) ? overridden : declarations[index].defaultValue;
                    if (!ShaderPropertyInspector.Compatible(property.type, value.kind)) { compatible = false; break; }
                    values.Add(value);
                }
                if (!compatible) continue;
                if (presentation.group != currentGroup)
                {
                    currentGroup = presentation.group;
                    Widget.SectionHeader(currentGroup.Length == 0 ? "Common Parameters" : currentGroup);
                }
                bool mixed = values.Skip(1).Any(value => !value.Equals(values[0]));
                UI.PushID(property.id.value);
                try
                {
                    if (mixed) Widget.Hint(property.displayName + " · Mixed values (editing applies to all selected Materials)");
                    ShaderPropertyInspector.Draw(context, target, "materials." + property.id.value, property, values[0],
                        value =>
                        {
                            int selectedIndex = 0;
                            foreach (MaterialAsset material in materials.Values)
                                material.Set(property.id, ShaderPropertyInspector.ApplyEdit(property.type, values[0], value, values[selectedIndex++]));
                        }, edits, readOnly, presentation);
                    if (mixed && UI.SmallButton("Use First Value for Selection"))
                    {
                        foreach (MaterialAsset material in materials.Values) material.Set(property.id, values[0]);
                        documents.ReplaceMany(materials);
                    }
                    if (UI.SmallButton("Reset Selected Overrides"))
                    {
                        foreach (MaterialAsset material in materials.Values)
                            material.ReplaceProperties(material.properties.Where(value => value.id != property.id).ToArray());
                        documents.ReplaceMany(materials);
                    }
                }
                finally { UI.PopID(); }
            }
            if (materials.Values.Any(value => value.properties.Count != 0) && UI.Button("Reset All Selected Overrides"))
            {
                foreach (MaterialAsset material in materials.Values) material.ReplaceProperties([]);
                documents.ReplaceMany(materials);
            }
            if (!UI.IsAnyItemActive()) documents.CommitMany(target.assetIds);

            void SetTechnique(ShaderTechniqueId technique)
            {
                foreach (MaterialAsset material in materials.Values) material.techniqueId = technique;
                documents.ReplaceMany(materials);
            }
        }
        finally { UI.EndDisabled(); UI.PopID(); }
    }

    private sealed class SelectionEdits(MaterialDocuments documents, IReadOnlyDictionary<Guid, MaterialAsset> materials) : IInspectionPropertyEditService
    {
        public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
        { mutation(); documents.ReplaceMany(materials, !UI.IsAnyItemActive()); return true; }
    }
}
