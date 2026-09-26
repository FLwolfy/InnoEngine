using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Inspection;
using Inno.Rendering;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Shaders;

[InspectionDrawer(typeof(AssetInspectionSelection))]
internal sealed class MaterialSelectionDrawer(IInspectionIconProvider<AssetFileEntry> icons) : InspectionDrawer<AssetInspectionSelection>
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
protected override string GetIcon(InspectionDrawContext context, AssetInspectionSelection target)
    {
        Guid id = target.assetIds.FirstOrDefault();
        return id != Guid.Empty
            && context.interactions.TryGetModule<MaterialDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(id, out AssetInfo? info) && info is not null
            && documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry) ? icons.GetIcon(entry) : icon;
    }
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
protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, AssetInspectionSelection target)
        => (target.assetIds.Count + " Assets", null);

    /// <summary>
    /// Renders the header presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
protected override void DrawHeader(InspectionDrawContext context, AssetInspectionSelection target)
    {
        if (!TryOpenDrafts(context, target, out MaterialDocuments? documents, out var drafts)) return;
        bool readOnly = drafts.Any(value => value.readOnly);
        ImGuiApi.BeginDisabled(readOnly);
        try
        {
            if (ImGuiApi.Button("Save Selected"))
            {
                documents!.CommitMany(target.assetIds);
                foreach (var draft in drafts) _ = context.interactions.documents.Save(draft.documentId);
            }
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Revert Selected"))
            {
                documents!.CommitMany(target.assetIds);
                using var transaction = context.interactions.history.BeginTransaction("Revert Materials");
                foreach (var draft in drafts) _ = context.interactions.documents.Revert(draft.documentId);
                transaction.Commit();
            }
        }
        finally { ImGuiApi.EndDisabled(); }
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
protected override void Draw(InspectionDrawContext context, AssetInspectionSelection target)
    {
        if (!TryOpenDrafts(context, target, out MaterialDocuments? documents, out var drafts))
        { Widget.Hint("Select Materials with compatible Shader interfaces to edit their common parameters."); return; }
        MaterialDocuments owner = documents!;
        var materials = drafts.ToDictionary(value => value.id, value => owner.Read(value.id));
        bool readOnly = drafts.Any(value => value.readOnly);
        ImGuiApi.PushID("materials." + string.Join(".", target.assetIds));
        ImGuiApi.BeginDisabled(readOnly);
        try
        {
            Widget.Hint("Save runs per document. Unsaved edits do not change Scene/Game.");
            if (readOnly) Widget.Hint("Selection includes installed read-only content. Copy it to the project before editing.");
            foreach (var draft in drafts.Where(value => value.error.Length != 0))
                Widget.Hint(draft.path.localPath + ": " + draft.error);
            var edits = new SelectionEdits(owner, materials);
            MaterialAsset first = materials.Values.First();
            bool selectionOpen = Widget.SectionHeader("Material Selection", "Changes are recorded together. Changing Shader retains each Material's independent overrides.");
            if (selectionOpen)
            {
                if (materials.Values.Any(value => value.shader?.identity.persistentId != first.shader?.identity.persistentId))
                    Widget.Hint("Shader · Mixed values");
                context.properties.DrawValue(context.editorContext, target, "materials.shader", "Shader", typeof(ShaderAsset),
                    () => first.shader, value =>
                    {
                        foreach (MaterialAsset material in materials.Values) material.shader = (ShaderAsset?)value;
                    }, edits, readOnly);
            }
            if (first.shader is null || first.shader.isMissing || first.shader.definition is not { } firstDefinition)
            {
                if (selectionOpen) Widget.Hint("A selected Shader is unavailable.");
                return;
            }
            ShaderTechniqueId[] commonTechniques = firstDefinition.techniques.Select(value => value.id)
                .Where(id => materials.Values.All(material => material.shader is { isMissing: false, definition: { } definition }
                    && definition.techniques.Any(technique => technique.id == id))).ToArray();
            bool mixedTechnique = materials.Values.Any(value => value.techniqueId != first.techniqueId);
            if (selectionOpen && (commonTechniques.Length > 1 || materials.Values.Any(value => value.techniqueId.isValid)))
            {
                string current = mixedTechnique ? "Mixed Techniques" : first.techniqueId.isValid ? first.techniqueId.value : "Automatic";
                if (Widget.BeginBoundedCombo("##materials.technique", current))
                {
                    try
                    {
                        if (ImGuiApi.Selectable("Automatic", !mixedTechnique && !first.techniqueId.isValid)) SetTechnique(default);
                        foreach (ShaderTechniqueId technique in commonTechniques)
                            if (ImGuiApi.Selectable(technique.value, !mixedTechnique && first.techniqueId == technique)) SetTechnique(technique);
                    }
                    finally { ImGuiApi.EndCombo(); }
        }
    }

            foreach (ShaderKeywordDefinition keyword in selectionOpen ? firstDefinition.keywords : [])
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
                    if (ImGuiApi.Selectable("None", !mixed && selected == "None")) SetOption(null);
                    foreach (string option in keyword.options)
                        if (ImGuiApi.Selectable(option, !mixed && selected == option)) SetOption(option);
                }
                finally { ImGuiApi.EndCombo(); }
                void SetOption(string? next)
                {
                    foreach (MaterialAsset material in materials.Values)
                        foreach (string option in keyword.options) material.SetKeyword(option, option == next);
                    owner.ReplaceMany(materials);
                }
            }
            bool parametersOpen = Widget.SectionHeader("Common Parameters", "Only compatible Material-owned inputs appear here. Unrelated overrides are retained per Material.");
            bool groupOpen = parametersOpen;
            string currentGroup = "";
            foreach (ShaderPropertyDefinition property in firstDefinition.properties)
            {
                if (property.bindingOwner != ShaderPropertyBindingOwner.Material
                    || property.bindingKind is not (ShaderPropertyBindingKind.Uniform or ShaderPropertyBindingKind.SampledTexture)) continue;
                var values = new List<MaterialValue>();
                ShaderParameterPresentation presentation = owner.Presentation(first.shader, property.id, out string presentationError);
                if (presentationError.Length != 0) Widget.Hint("Parameter presentation unavailable: " + presentationError);
                if (!presentation.visible) continue;
                bool compatible = true;
                foreach (MaterialAsset material in materials.Values)
                {
                    ShaderPropertyDefinition[] declarations = material.shader is { isMissing: false } shader ? shader.definition?.properties ?? [] : [];
                    int index = Array.FindIndex(declarations, value => value.id == property.id && value.type == property.type
                        && value.bindingKind == property.bindingKind && value.bindingOwner == property.bindingOwner);
                    if (index < 0) { compatible = false; break; }
                    ShaderParameterPresentation other = owner.Presentation(material.shader!, property.id, out _);
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
                    groupOpen = Widget.SectionHeader(currentGroup.Length == 0 ? "Common Parameters" : currentGroup);
                }
                if (!groupOpen) continue;
                bool mixed = values.Skip(1).Any(value => !value.Equals(values[0]));
                ImGuiApi.PushID(property.id.value);
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
                    if (mixed && ImGuiApi.SmallButton("Use First Value for Selection"))
                    {
                        foreach (MaterialAsset material in materials.Values) material.Set(property.id, values[0]);
                        owner.ReplaceMany(materials);
                    }
                    if (ImGuiApi.SmallButton("Reset Selected Overrides"))
                    {
                        foreach (MaterialAsset material in materials.Values)
                            material.ReplaceProperties(material.properties.Where(value => value.id != property.id).ToArray());
                        owner.ReplaceMany(materials);
                    }
                }
                finally { ImGuiApi.PopID(); }
            }
            if (parametersOpen && materials.Values.Any(value => value.properties.Count != 0) && ImGuiApi.Button("Reset All Selected Overrides"))
            {
                foreach (MaterialAsset material in materials.Values) material.ReplaceProperties([]);
                owner.ReplaceMany(materials);
            }
            if (!ImGuiApi.IsAnyItemActive()) owner.CommitMany(target.assetIds);

            void SetTechnique(ShaderTechniqueId technique)
            {
                foreach (MaterialAsset material in materials.Values) material.techniqueId = technique;
                owner.ReplaceMany(materials);
            }
        }
        finally { ImGuiApi.EndDisabled(); ImGuiApi.PopID(); }
    }

    private static bool TryOpenDrafts(
        InspectionDrawContext context,
        AssetInspectionSelection target,
        out MaterialDocuments? documents,
        out List<Inno.Editor.Assets.AssetDraftDocuments<MaterialAsset>.Draft> drafts)
    {
        drafts = [];
        if (!context.interactions.TryGetModule(out documents) || documents is null) return false;
        foreach (Guid id in target.assetIds)
        {
            if (!documents.assets.TryGetInfo(id, out AssetInfo? info) || info is null
                || !documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry)
                || !entry.extension.Equals(".imaterial", StringComparison.OrdinalIgnoreCase))
                return false;
            var draft = documents.OpenDraft(info.assetPath);
            documents.TouchInspection(draft);
            drafts.Add(draft);
        }
        return drafts.Count != 0;
    }

    private sealed class SelectionEdits(MaterialDocuments documents, IReadOnlyDictionary<Guid, MaterialAsset> materials) : IInspectionPropertyEditService
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
        { mutation(); documents.ReplaceMany(materials, !ImGuiApi.IsAnyItemActive()); return true; }
    }
}
