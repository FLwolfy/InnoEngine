using Inno.Assets;
using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Shows metadata for the current selected asset entry.
/// </summary>
public sealed class InspectorPanel : EditorPanel
{
    /// <summary>
    /// Creates the panel.
    /// </summary>
    public InspectorPanel()
        : base("asset.inspector", "Inspector")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        string? selectedPath = context.selection.selectedPath;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            ImGuiWidget.Hint("Select an item from Asset Tree or Assets panel.");
            return;
        }

        if (!AssetManager.TryGetFileSystemEntry(selectedPath, out AssetFileEntry entry))
        {
            NativeImGui.TextColored(new System.Numerics.Vector4(1f, 0.35f, 0.35f, 1f), "Selected entry no longer exists.");
            return;
        }

        NativeImGui.TextUnformatted("Path");
        NativeImGui.Separator();
        NativeImGui.TextUnformatted(entry.relativePath);

        NativeImGui.Spacing();
        NativeImGui.TextUnformatted("Type");
        NativeImGui.Separator();
        NativeImGui.TextUnformatted(entry.isDirectory ? "Directory" : "File");

        if (!entry.isDirectory)
        {
            NativeImGui.Spacing();
            NativeImGui.TextUnformatted("Extension");
            NativeImGui.Separator();
            NativeImGui.TextUnformatted(string.IsNullOrEmpty(entry.extension) ? "<none>" : entry.extension);
        }
    }
}
