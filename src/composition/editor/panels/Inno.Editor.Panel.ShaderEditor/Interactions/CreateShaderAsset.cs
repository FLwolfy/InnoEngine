using System;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Editor.Interactions;
using Inno.Editor.Panel.FileBrowser;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

[EditorAction("shader/create-asset", "panel/asset.file-browser")]
[EditorMenu("panel/asset.file-browser", "Create/Shader", order: 130)]
internal sealed class CreateShaderAsset(ShaderEditorDocuments documents, AssetEditorModule browser) : EditorAction<string>
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext<string> context)
    {
        AssetPath path = AssetPath.Parse(context.target);
        return documents.assets.sourceMounts.Any(mount => mount.id == path.source && !mount.isReadOnly)
            ? EditorActionState.enabled : EditorActionState.disabled;
    }
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<string> context)
    {
        AssetPath parent = AssetPath.Parse(context.target);
        AssetSourceMount mount = documents.assets.sourceMounts.Single(mount => mount.id == parent.source);
        string prefix = parent.localPath.TrimEnd('/');
        if (prefix.Length != 0) prefix += "/";
        int index = 0;
        AssetPath path;
        do { path = new(parent.source, prefix + (index++ == 0 ? "New Shader" : "New Shader " + index) + ".ishader"); }
        while (File.Exists(mount.Resolve(path.localPath)) || File.Exists(mount.Resolve(path.localPath) + ".imeta"));
        GraphDocument graph = ShaderGraphTemplates.CreateRaster(documents.serialization, documents.context);
        AssetFileEntry created = browser.CreateSource(path, GraphDocumentCodec.Encode(graph, documents.serialization));
        context.interactions.SetSelection(created);
        context.interactions.OpenPanel("rendering.shader-editor");
    }
}
