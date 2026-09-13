using System;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Interactions;
using Inno.Editor.Panel.FileBrowser;
using Inno.Rendering;

namespace Inno.Editor.Panel.ShaderEditor;

[EditorAction("material/create", "panel/asset.file-browser")]
[EditorMenu("panel/asset.file-browser", "Create/Material", order: 140)]
internal sealed class CreateMaterialAsset(AssetPipeline assets, AssetEditorModule browser) : EditorAction<string>
{
    protected override EditorActionState Query(EditorActionContext<string> context)
        => assets.sourceMounts.Any(mount => mount.id == AssetPath.Parse(context.target).source && !mount.isReadOnly)
            ? EditorActionState.enabled : EditorActionState.disabled;
    protected override void Execute(EditorActionContext<string> context)
        => Create(assets, browser, context.interactions, AssetPath.Parse(context.target), null);

    internal static void Create(AssetPipeline assets, AssetEditorModule browser, EditorInteractions interactions, AssetPath parent, ShaderAsset? shader)
    {
        AssetSourceMount mount = assets.sourceMounts.Single(source => source.id == parent.source);
        string prefix = parent.localPath.TrimEnd('/');
        if (prefix.Length != 0) prefix += "/";
        int index = 1;
        AssetPath path;
        do { path = new(parent.source, prefix + "New Material" + (index == 1 ? "" : " " + index) + ".imaterial"); index++; }
        while (File.Exists(mount.Resolve(path.localPath)) || File.Exists(mount.Resolve(path.localPath) + ".imeta"));
        AssetFileEntry entry = browser.CreateSource(path, assets.CreateSourceStore().Encode(new MaterialAsset { shader = shader }));
        interactions.SetSelection(entry);
        interactions.OpenPanel("scene.inspector");
    }
}

[EditorAction("material/create-from-shader", "panel/asset.file-browser")]
[EditorMenu("panel/asset.file-browser", "Create Material From Shader", order: 141)]
internal sealed class CreateMaterialFromShader(AssetPipeline assets, AssetEditorModule browser) : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => context.target.extension == ".ishader" && !context.target.isReadOnly ? EditorActionState.enabled : EditorActionState.hidden;
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        ShaderAsset shader = assets.Load<ShaderAsset>(context.target.assetPath);
        string parent = Path.GetDirectoryName(context.target.assetPath.localPath)?.Replace('\\', '/') ?? "";
        CreateMaterialAsset.Create(assets, browser, context.interactions, new(context.target.assetPath.source, parent), shader);
    }
}
