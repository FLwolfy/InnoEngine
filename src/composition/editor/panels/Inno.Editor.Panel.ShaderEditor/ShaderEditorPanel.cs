using System;
using System.Numerics;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Shaders;
using Inno.Rendering;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

[EditorPanel("rendering.shader-editor", "Shader Editor", order: 230, menuPath: "Authoring")]
internal sealed class ShaderEditorPanel(ShaderEditorDocuments documents) : EditorPanel
{
    /// <inheritdoc />
    public override bool useWindowPadding => false;
    /// <inheritdoc />
    public override bool allowScrolling => false;
    /// <inheritdoc />
    public override Vector2 initialSize => new(960f, 640f);
    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        AssetFileEntry? entry = documents.interactions.selection.selectedTarget as AssetFileEntry;
        if (documents.interactions.selection.selectedTarget is ShaderAsset shader)
            _ = documents.assets.TryGetFileSystemEntry(shader.assetPath, out entry);
        if (documents.interactions.selection.selectedTarget is ShaderInspectionSelection selection
            && documents.assets.TryGetInfo(selection.assetId, out AssetInfo? info) && info is not null)
            _ = documents.assets.TryGetFileSystemEntry(info.assetPath, out entry);
        if (entry is null || entry.isDirectory || !entry.assetPath.localPath.EndsWith(".ishader", StringComparison.OrdinalIgnoreCase))
        {
            NativeImGui.TextDisabled("Select a Shader (.ishader) in the File Browser.");
            return;
        }
        try { new ShaderEditorCanvas(documents, documents.Open(entry)).Draw(); }
        catch (Exception failure) when ((failure is System.IO.IOException or InvalidOperationException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { NativeImGui.TextWrapped(failure.Message); }
    }
}

[EditorAction("editor/open", "panel/asset.file-browser", priority: 1000)]
internal sealed class OpenShaderEditorAction : EditorAction<AssetFileEntry>
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => !context.target.isDirectory && context.target.assetPath.localPath.EndsWith(".ishader", StringComparison.OrdinalIgnoreCase)
            ? EditorActionState.enabled : EditorActionState.hidden;
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        context.interactions.SetSelection(context.target);
        if (!context.interactions.OpenPanel("rendering.shader-editor")) throw new InvalidOperationException("Shader Editor is unavailable.");
    }
}
