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
        if (UI.SmallButton("Open Source")) OpenShaderFunction.Open(owner, sourceId);
        UI.SameLine();
        if (UI.SmallButton("Import Settings…"))
        {
            LoadSourceSettings(info.assetPath, sourceId);
            UI.OpenPopup("##source-settings");
        }
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
                if (UI.BeginCombo("Language", settings.languageId))
                {
                    foreach (string language in owner.frontends.languageIds)
                        if (UI.Selectable(language, settings.languageId == language)) { settings.languageId = language; changed = true; }
                    UI.EndCombo();
                }
                string implementation = settings.implementationId;
                if (UI.InputText("Adapter implementation", ref implementation, 256)) { settings.implementationId = implementation; changed = true; }
                string function = settings.entryPoint;
                if (UI.InputText("Exported function", ref function, 256)) { settings.entryPoint = function; changed = true; }
                UI.TextWrapped("Inputs, outputs and structure members are parsed from this function declaration. Alternative implementations must expose the same interface.");
                UI.SeparatorText("Alternative Implementations");
                for (int i = 0; i < settings.implementations.Length; i++)
                {
                    UI.PushID(i);
                    ShaderFunctionAsset? current = settings.implementations[i];
                    if (UI.BeginCombo("##implementation", current?.assetPath.ToString() ?? "Missing source"))
                    {
                        foreach (AssetFileEntry source in owner.assets.GetFileSystemEntries(includeDirectories: false))
                            if (source.extension == ".ishadersource" && source.assetPath != info.assetPath
                                && UI.Selectable(source.assetPath.ToString(), current?.assetPath == source.assetPath)
                                && owner.assets.TryLoad(source.assetPath, out ShaderFunctionAsset? replacement) && replacement is not null)
                            { settings.implementations[i] = replacement; changed = true; }
                        UI.EndCombo();
                    }
                    UI.SameLine();
                    if (UI.SmallButton("Remove"))
                    {
                        settings.implementations = settings.implementations.Where((_, index) => index != i).ToArray();
                        changed = true;
                        UI.PopID();
                        break;
                    }
                    UI.PopID();
                }
                if (UI.Button("Add Implementation"))
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
