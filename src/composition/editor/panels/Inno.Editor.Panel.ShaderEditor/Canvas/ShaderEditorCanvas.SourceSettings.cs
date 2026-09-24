using System;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Native.ImGui;
using Inno.Rendering.Assets;
using EditorImGui = Inno.Editor.ImGui.ImGui;
using ImGuiApi = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void SourceSettings(Guid sourceId)
    {
        if (!owner.assets.TryGetInfo(sourceId, out AssetInfo? info) || info is null) return;
        InspectorRow("source.actions", "Source", () =>
        {
            if (ImGuiApi.SmallButton("Show in File Browser")
                && owner.interactions.TryGetModule<Inno.Editor.Panel.FileBrowser.AssetEditorModule>(out var browser) && browser is not null)
                RevealShaderFunction.Reveal(owner, browser, sourceId);
            ImGuiApi.SameLine();
            if (ImGuiApi.SmallButton("Import Settings…"))
            {
                LoadSourceSettings(info.assetPath, sourceId);
                ImGuiApi.OpenPopup("##source-settings");
            }
        });
        ImGuiApi.SetNextWindowSize(new(580, 470), ImGuiCond.Appearing);
        if (!ImGuiApi.BeginPopup("##source-settings")) return;
        try
        {
            ImGuiApi.TextUnformatted(info.assetPath.ToString());
            ImGuiApi.Separator();
            if (draft.settingsSource != sourceId) LoadSourceSettings(info.assetPath, sourceId);
            bool readOnly = !owner.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry) || entry.isReadOnly;
            ImGuiApi.BeginDisabled(readOnly);
            try
            {
                var settings = owner.serialization.Deserialize<ShaderSourceImportSettings>(draft.sourceSettings, owner.context);
                bool changed = false;
                InspectorRow("source.language", "Language", () =>
                {
                    if (!Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.BeginBoundedCombo("##language", settings.languageId)) return;
                    try
                    {
                        foreach (string language in owner.frontends.languageIds)
                            if (ImGuiApi.Selectable(language, settings.languageId == language)) { settings.languageId = language; changed = true; }
                    }
                    finally { ImGuiApi.EndCombo(); }
                });
                string implementation = settings.implementationId;
                bool implementationChanged = false;
                InspectorRow("source.adapter", "Adapter", () => implementationChanged = EditorImGui.InputText("##adapter", ref implementation, 256));
                if (implementationChanged) { settings.implementationId = implementation; changed = true; }
                string catalog = settings.catalogPath;
                bool catalogChanged = false;
                InspectorRow("source.catalog", "Catalog Path", () => catalogChanged = EditorImGui.InputText("##catalog", ref catalog, 256));
                if (catalogChanged) { settings.catalogPath = catalog; changed = true; }
                int catalogOrder = settings.catalogOrder;
                bool orderChanged = false;
                InspectorRow("source.catalog-order", "Catalog Order", () => orderChanged = ImGuiApi.InputInt("##catalog-order", ref catalogOrder));
                if (orderChanged) { settings.catalogOrder = catalogOrder; changed = true; }
                ImGuiApi.TextWrapped("Catalog paths are declared by the source library and group its exported functions in Shader creation tools. They do not affect compilation or runtime assets.");
                ImGuiApi.SeparatorText("Exported Functions");
                for (int i = 0; i < settings.exports.Length; i++)
                {
                    ImGuiApi.PushID("export." + i);
                    string function = settings.exports[i];
                    bool functionChanged = false, remove = false;
                    InspectorRow("source.export", "Function " + (i + 1), () =>
                    {
                        functionChanged = EditorImGui.InputText("##name", ref function, 256);
                        ImGuiApi.SameLine();
                        remove = ImGuiApi.SmallButton("Remove");
                    });
                    if (functionChanged) { settings.exports[i] = function; changed = true; }
                    if (remove)
                    {
                        settings.exports = settings.exports.Where((_, index) => index != i).ToArray();
                        changed = true;
                        ImGuiApi.PopID();
                        break;
                    }
                    ImGuiApi.PopID();
                }
                if (CenteredAddButton("Add Export")) { settings.exports = [.. settings.exports, "Function"]; changed = true; }
                ImGuiApi.TextWrapped("Every listed name is exported as an independent graph function. All other functions remain private helpers. Ports are parsed from each exported declaration; alternative implementations must expose matching interfaces.");
                ImGuiApi.SeparatorText("Alternative Implementations");
                for (int i = 0; i < settings.implementations.Length; i++)
                {
                    ImGuiApi.PushID(i);
                    ShaderFunctionAsset? current = settings.implementations[i];
                    bool remove = false;
                    InspectorRow("source.implementation", "Implementation " + (i + 1), () =>
                    {
                        if (Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.BeginBoundedCombo("##implementation", current?.assetPath.ToString() ?? "Missing source"))
                        {
                            try
                            {
                                foreach (AssetFileEntry source in owner.assets.GetFileSystemEntries(includeDirectories: false))
                                    if (source.extension == ".ishadersource" && source.assetPath != info.assetPath
                                        && ImGuiApi.Selectable(source.assetPath.ToString(), current?.assetPath == source.assetPath)
                                        && owner.assets.TryLoad(source.assetPath, out ShaderFunctionAsset? replacement) && replacement is not null)
                                    { settings.implementations[i] = replacement; changed = true; }
                            }
                            finally { ImGuiApi.EndCombo(); }
                        }
                        ImGuiApi.SameLine();
                        remove = ImGuiApi.SmallButton("Remove");
                    });
                    if (remove)
                    {
                        settings.implementations = settings.implementations.Where((_, index) => index != i).ToArray();
                        changed = true;
                        ImGuiApi.PopID();
                        break;
                    }
                    ImGuiApi.PopID();
                }
                if (CenteredAddButton("Add Implementation"))
                { settings.implementations = [.. settings.implementations, null!]; changed = true; }
                if (changed) draft.sourceSettings = owner.serialization.Serialize(settings, owner.context);
                ImGuiApi.Separator();
                if (ImGuiApi.Button("Apply Import Settings"))
                {
                    bool imported = owner.importSettings.Apply(info.assetPath, settings, draft.sourceSettingsFingerprint);
                    LoadSourceSettings(info.assetPath, sourceId);
                    draft.sourceSettingsStatus = imported ? "Applied · ports refreshed" : "Settings saved · import failed; see diagnostics";
                    draft.portRevision = ulong.MaxValue;
                }
            }
            finally { ImGuiApi.EndDisabled(); }
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Reload")) LoadSourceSettings(info.assetPath, sourceId);
            if (readOnly) ImGuiApi.TextDisabled("Installed source settings are read-only.");
            if (draft.sourceSettingsStatus.Length != 0) ImGuiApi.TextWrapped(draft.sourceSettingsStatus);
        }
        catch (Exception failure) when ((failure is IOException or InvalidOperationException or ArgumentException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { draft.sourceSettingsStatus = failure.Message; ImGuiApi.TextWrapped(draft.sourceSettingsStatus); }
        finally { ImGuiApi.EndPopup(); }
    }

    private void LoadSourceSettings(AssetPath path, Guid sourceId)
    {
        AssetImportSettingsSnapshot snapshot = owner.assets.GetImportSettings(path);
        if (snapshot.value is not ShaderSourceImportSettings settings) throw new InvalidOperationException("The source importer settings are unavailable.");
        draft.settingsSource = sourceId;
        draft.sourceSettingsFingerprint = snapshot.fingerprint;
        draft.sourceSettings = owner.serialization.Serialize(settings, owner.context);
        draft.sourceSettingsStatus = "";
    }
}
