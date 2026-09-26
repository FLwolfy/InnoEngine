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
    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
public override string icon => "S";
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
protected override string GetIcon(InspectionDrawContext context, AssetFileEntry target) => icons.GetIcon(target);
    /// <summary>
    /// Checks whether this drawer supports the selected Inspector target.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the documented condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
protected override bool CanInspect(AssetFileEntry target)
        => !target.isDirectory && target.extension.Equals(".ishader", StringComparison.OrdinalIgnoreCase);
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
protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, AssetFileEntry target)
        => (target.nameWithoutExtension, null);
    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
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
    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
public override string icon => "S";
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
protected override string GetIcon(InspectionDrawContext context, ShaderInspectionSelection target)
        => context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.assetId, out AssetInfo? info) && info is not null
            && documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry) ? icons.GetIcon(entry) : icon;
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
protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, ShaderInspectionSelection target)
    {
        if (context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetInfo(target.assetId, out AssetInfo? info) && info is not null)
            return (Path.GetFileName(info.assetPath.localPath), null);
        return ("Shader", null);
    }
    /// <summary>
    /// Renders the header presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
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
    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
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
    /// <summary>
    /// Gets the icon glyph used to represent this item in the editor.
    /// </summary>
public override string icon => "S";
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
protected override string GetIcon(InspectionDrawContext context, ShaderAsset target)
        => context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) && documents is not null
            && documents.assets.TryGetFileSystemEntry(target.assetPath, out AssetFileEntry entry) ? icons.GetIcon(entry) : icon;
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
protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, ShaderAsset target) => (target.name, null);
    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
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
