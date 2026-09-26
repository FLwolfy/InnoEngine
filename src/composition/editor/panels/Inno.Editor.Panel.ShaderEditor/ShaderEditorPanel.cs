using System;
using System.Numerics;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.Shaders;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Rendering;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

[EditorPanel("rendering.shader-editor", "Shader Editor", order: 230, menuPath: "Authoring")]
internal sealed class ShaderEditorPanel(ShaderEditorDocuments documents) : EditorPanel
{
    /// <summary>
    /// Gets whether use window padding is enabled for this implementation.
    /// </summary>
    public override bool useWindowPadding => true;
    /// <summary>
    /// Gets whether allow scrolling is enabled for this implementation.
    /// </summary>
    public override bool allowScrolling => false;
    /// <summary>
    /// Gets the preferred initial window size in logical editor units.
    /// </summary>
    public override Vector2 initialSize => new(960f, 640f);
    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
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
            ShaderEditorCanvas.DrawHeader(documents, null);
            ImGuiWidget.CenteredWrappedText(
                "Select a Shader (.ishader) in the File Browser.",
                Vector2.Max(Vector2.One, NativeImGui.GetContentRegionAvail()),
                new Vector2(40f, 28f));
            return;
        }
        try { new ShaderEditorCanvas(documents, documents.Open(entry)).Draw(); }
        catch (Exception failure) when ((failure is System.IO.IOException or InvalidOperationException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        { NativeImGui.TextWrapped(failure.Message); }
    }
}

[EditorAction("editor/open", priority: 1000)]
internal sealed class OpenShaderEditorAction : EditorAction<ShaderAsset, string>
{
    /// <summary>
    /// Evaluates the operation's current availability and presentation state.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<ShaderAsset, string> context)
        => EditorActionState.enabled;
    /// <summary>
    /// Applies the editor action to the supplied interaction context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void Execute(EditorActionContext<ShaderAsset, string> context)
    {
        context.interactions.SetSelection(context.target);
        if (!context.interactions.OpenPanel("rendering.shader-editor")) throw new InvalidOperationException("Shader Editor is unavailable.");
    }
}
