using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui;
using Inno.Platform.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Inspector;

[InspectorDrawer(typeof(AssetFileEntry))]
internal sealed class AssetSelectionInspectorDrawer : InspectorDrawer<AssetFileEntry>
{
    public override string icon => ImGuiIcon.File;

    protected override string GetName(InspectorDrawContext context, AssetFileEntry target)
        => target.nameWithoutExtension;

    protected override void DrawHeader(InspectorDrawContext context, AssetFileEntry target)
        => NativeImGui.TextUnformatted(target.relativePath);

    protected override void Draw(InspectorDrawContext context, AssetFileEntry entry)
    {
        DrawMetadata("Type", entry.isDirectory ? "Directory" : "File");
        if (!entry.isDirectory)
        {
            DrawMetadata("Extension", string.IsNullOrEmpty(entry.extension) ? "<none>" : entry.extension);
        }
    }

    private static void DrawMetadata(string label, string value)
    {
        NativeImGui.TextUnformatted(label);
        NativeImGui.Separator();
        NativeImGui.TextUnformatted(value);
        NativeImGui.Spacing();
    }
}
