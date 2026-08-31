using Inno.Platform.ImGui;
using Inno.Editor.Settings;

namespace Inno.Editor.Panel.FileBrowser;

[AssetIcon(".txt", ImGuiIcon.FileLines)]
[AssetIcon(".json", ImGuiIcon.FileLines)]
[AssetIcon(".yaml", ImGuiIcon.FileLines)]
[AssetIcon(".yml", ImGuiIcon.FileLines)]
[AssetIcon(".md", ImGuiIcon.FileLines)]
[AssetIcon(".xml", ImGuiIcon.FileLines)]
[AssetIcon(".bytes", ImGuiIcon.File)]
[AssetIcon(".bin", ImGuiIcon.File)]
[AssetIcon(".dat", ImGuiIcon.File)]
[AssetIcon(".iscene", "Global/Appearance/Icons/Scene")]
[AssetIcon(".iprefab", "Global/Appearance/Icons/Prefab")]
[AssetIcon(".cs", ImGuiIcon.FileCode)]
[AssetIcon(".dll", ImGuiIcon.Plug)]
[AssetIcon(".iasmdef", ImGuiIcon.Gears)]
internal static class BuiltInAssetIcons;
