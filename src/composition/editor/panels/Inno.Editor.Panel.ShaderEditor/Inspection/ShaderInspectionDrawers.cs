using System;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Inspection;
using Inno.Editor.Shaders;
using Inno.Rendering;
using UI = Inno.Native.ImGui.ImGui;

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
            new ShaderEditorCanvas(documents!, documents!.Open(target)).DrawInspector(context, []);
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
        => (target.nodes.Count == 0 ? "Shader" : target.nodes.Count == 1 ? "Shader Node" : "Shader Nodes", null);
    protected override void Draw(InspectionDrawContext context, ShaderInspectionSelection target)
    {
        if (!context.interactions.TryGetModule<ShaderEditorDocuments>(out var documents) || documents is null) return;
        if (!documents.assets.TryGetInfo(target.assetId, out AssetInfo? info) || info is null
            || !documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry))
        { UI.TextWrapped("Shader source unavailable. Selection identities are retained."); return; }
        new ShaderEditorCanvas(documents, documents.Open(entry)).DrawInspector(context, target.nodes);
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
        new ShaderEditorCanvas(documents, documents.Open(entry)).DrawInspector(context, []);
    }
}
