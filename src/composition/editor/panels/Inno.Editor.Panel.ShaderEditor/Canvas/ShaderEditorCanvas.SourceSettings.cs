using System;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Native.ImGui;
using Inno.Rendering.Assets;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void SourceSettings(Guid sourceId)
    {
        if (!owner.assets.TryGetInfo(sourceId, out AssetInfo? info) || info is null) return;
        InspectorRow("source.actions", "Source", () =>
        {
            if (UI.SmallButton("Show in File Browser")
                && owner.interactions.TryGetModule<Inno.Editor.Panel.FileBrowser.AssetEditorModule>(out var browser) && browser is not null)
                RevealShaderFunction.Reveal(owner, browser, sourceId);
            UI.SameLine();
            if (UI.SmallButton("Import Settings…"))
            {
                LoadSourceSettings(info.assetPath, sourceId);
                UI.OpenPopup("##source-settings");
            }
        });
        UI.SetNextWindowSize(new(580, 470), ImGuiCond.Appearing);
        if (!UI.BeginPopup("##source-settings")) return;
        try
        {
            UI.TextUnformatted(info.assetPath.ToString());
            UI.Separator();
            if (draft.settingsSource != sourceId) LoadSourceSettings(info.assetPath, sourceId);
            bool readOnly = !owner.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry) || entry.isReadOnly;
            UI.BeginDisabled(readOnly);
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
                            if (UI.Selectable(language, settings.languageId == language)) { settings.languageId = language; changed = true; }
                    }
                    finally { UI.EndCombo(); }
                });
                string implementation = settings.implementationId;
                bool implementationChanged = false;
                InspectorRow("source.adapter", "Adapter", () => implementationChanged = UI.InputText("##adapter", ref implementation, 256));
                if (implementationChanged) { settings.implementationId = implementation; changed = true; }
                string catalog = settings.catalogPath;
                bool catalogChanged = false;
                InspectorRow("source.catalog", "Catalog Path", () => catalogChanged = UI.InputText("##catalog", ref catalog, 256));
                if (catalogChanged) { settings.catalogPath = catalog; changed = true; }
                int catalogOrder = settings.catalogOrder;
                bool orderChanged = false;
                InspectorRow("source.catalog-order", "Catalog Order", () => orderChanged = UI.InputInt("##catalog-order", ref catalogOrder));
                if (orderChanged) { settings.catalogOrder = catalogOrder; changed = true; }
                UI.TextWrapped("Catalog paths are declared by the source library and group its exported functions in Shader creation tools. They do not affect compilation or runtime assets.");
                UI.SeparatorText("Exported Functions");
                for (int i = 0; i < settings.exports.Length; i++)
                {
                    UI.PushID("export." + i);
                    string function = settings.exports[i];
                    bool functionChanged = false, remove = false;
                    InspectorRow("source.export", "Function " + (i + 1), () =>
                    {
                        functionChanged = UI.InputText("##name", ref function, 256);
                        UI.SameLine();
                        remove = UI.SmallButton("Remove");
                    });
                    if (functionChanged) { settings.exports[i] = function; changed = true; }
                    if (remove)
                    {
                        settings.exports = settings.exports.Where((_, index) => index != i).ToArray();
                        changed = true;
                        UI.PopID();
                        break;
                    }
                    UI.PopID();
                }
                if (CenteredAddButton("Add Export")) { settings.exports = [.. settings.exports, "Function"]; changed = true; }
                UI.TextWrapped("Every listed name is exported as an independent graph function. All other functions remain private helpers. Ports are parsed from each exported declaration; alternative implementations must expose matching interfaces.");
                UI.SeparatorText("Alternative Implementations");
                for (int i = 0; i < settings.implementations.Length; i++)
                {
                    UI.PushID(i);
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
                                        && UI.Selectable(source.assetPath.ToString(), current?.assetPath == source.assetPath)
                                        && owner.assets.TryLoad(source.assetPath, out ShaderFunctionAsset? replacement) && replacement is not null)
                                    { settings.implementations[i] = replacement; changed = true; }
                            }
                            finally { UI.EndCombo(); }
                        }
                        UI.SameLine();
                        remove = UI.SmallButton("Remove");
                    });
                    if (remove)
                    {
                        settings.implementations = settings.implementations.Where((_, index) => index != i).ToArray();
                        changed = true;
                        UI.PopID();
                        break;
                    }
                    UI.PopID();
                }
                if (CenteredAddButton("Add Implementation"))
                { settings.implementations = [.. settings.implementations, null!]; changed = true; }
                if (changed) draft.sourceSettings = owner.serialization.Serialize(settings, owner.context);
                UI.Separator();
                if (UI.Button("Apply Import Settings"))
                {
                    bool imported = owner.importSettings.Apply(info.assetPath, settings, draft.sourceSettingsFingerprint);
                    LoadSourceSettings(info.assetPath, sourceId);
                    draft.sourceSettingsStatus = imported ? "Applied · ports refreshed" : "Settings saved · import failed; see diagnostics";
                    draft.portRevision = ulong.MaxValue;
                }
            }
            finally { UI.EndDisabled(); }
            UI.SameLine();
            if (UI.Button("Reload")) LoadSourceSettings(info.assetPath, sourceId);
            if (readOnly) UI.TextDisabled("Installed source settings are read-only.");
            if (draft.sourceSettingsStatus.Length != 0) UI.TextWrapped(draft.sourceSettingsStatus);
        }
        catch (Exception failure) when ((failure is IOException or InvalidOperationException or ArgumentException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { draft.sourceSettingsStatus = failure.Message; UI.TextWrapped(draft.sourceSettingsStatus); }
        finally { UI.EndPopup(); }
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
