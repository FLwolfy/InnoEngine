using System;
using System.IO;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Editor.ImGui;
using Inno.Editor.Inspection;
using Inno.Editor.Shaders;
using Inno.Rendering;
using ImGuiApi = Inno.Native.ImGui.ImGui;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.ShaderEditor;

[InspectionDrawer(typeof(AssetFileEntry), priority: 100, conditional: true)]
internal sealed class ShaderSourceDrawer(IInspectionIconProvider<AssetFileEntry> icons) : InspectionDrawer<AssetFileEntry>
{
    public override string icon => "S";
    protected override string GetIcon(InspectionDrawContext context, AssetFileEntry target) => icons.GetIcon(target);
    protected override bool CanInspect(AssetFileEntry target)
        => !target.isDirectory && target.extension.Equals(".ishader", StringComparison.OrdinalIgnoreCase);
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, AssetFileEntry target)
        => (target.nameWithoutExtension, null);
    protected override void Draw(InspectionDrawContext context, AssetFileEntry target)
    {
        if (context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents))
        {
            if (documents!.TryOpen(target, out ShaderEditorDocuments.Draft draft))
                new ShaderEditorCanvas(documents, draft).DrawInspector(context, []);
            else
                ImGuiApi.TextDisabled("Waiting for Shader asset import…");
        }
    }
}

[InspectionDrawer(typeof(ShaderInspectionSelection))]
internal sealed class ShaderSelectionDrawer(IInspectionIconProvider<AssetFileEntry> icons) : InspectionDrawer<ShaderInspectionSelection>
{
    public override string icon => "S";
    protected override string GetIcon(InspectionDrawContext context, ShaderInspectionSelection target)
        => context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.assetId, out AssetInfo? info) && info is not null
            && documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry) ? icons.GetIcon(entry) : icon;
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, ShaderInspectionSelection target)
    {
        if (context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.assetId, out AssetInfo? info) && info is not null)
            return (Path.GetFileName(info.assetPath.localPath), null);
        return ("Shader", null);
    }
    protected override void DrawHeader(InspectionDrawContext context, ShaderInspectionSelection target)
    {
        if (!context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) || documents is null
            || !documents.assets.TryGetInfo(target.assetId, out AssetInfo? info) || info is null
            || !documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry))
        {
            Widget.ColoredText(EditorPalette.assetBreadcrumbText, "Node selection unavailable");
            return;
        }
        if (!documents.TryOpen(entry, out ShaderEditorDocuments.Draft draft))
        {
            Widget.ColoredText(EditorPalette.assetBreadcrumbText, "Waiting for Shader asset import…");
            return;
        }
        if (target.nodes.Count == 1 && documents.Controller(draft).document.FindNode(target.nodes[0]) is GraphNodeRecord node)
            Widget.ColoredText(EditorPalette.assetBreadcrumbText, "Node: " + new ShaderEditorCanvas(documents, draft).Title(node));
        else
            Widget.ColoredText(EditorPalette.assetBreadcrumbText, $"Nodes: {target.nodes.Count} selected");
    }
    protected override void Draw(InspectionDrawContext context, ShaderInspectionSelection target)
    {
        if (!context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) || documents is null) return;
        if (!documents.assets.TryGetInfo(target.assetId, out AssetInfo? info) || info is null
            || !documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry))
        { ImGuiApi.TextWrapped("Shader source unavailable. Selection identities are retained."); return; }
        if (documents.TryOpen(entry, out ShaderEditorDocuments.Draft draft))
            new ShaderEditorCanvas(documents, draft).DrawInspector(context, target.nodes);
        else
            ImGuiApi.TextDisabled("Waiting for Shader asset import…");
    }
}

[InspectionDrawer(typeof(ShaderAsset))]
internal sealed class ShaderAssetDrawer(IInspectionIconProvider<AssetFileEntry> icons) : InspectionDrawer<ShaderAsset>
{
    public override string icon => "S";
    protected override string GetIcon(InspectionDrawContext context, ShaderAsset target)
        => context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetFileSystemEntry(target.assetPath, out AssetFileEntry entry) ? icons.GetIcon(entry) : icon;
    protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, ShaderAsset target) => (target.name, null);
    protected override void Draw(InspectionDrawContext context, ShaderAsset target)
    {
        if (!context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) || documents is null) return;
        if (!documents.assets.TryGetFileSystemEntry(target.assetPath, out AssetFileEntry entry)) return;
        if (documents.TryOpen(entry, out ShaderEditorDocuments.Draft draft))
            new ShaderEditorCanvas(documents, draft).DrawInspector(context, []);
        else
            ImGuiApi.TextDisabled("Waiting for Shader asset import…");
    }
}
