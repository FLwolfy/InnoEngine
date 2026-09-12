using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Editor.Interactions;
using Inno.Editor.Panel.FileBrowser;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

internal struct ShaderCanvasGroup
{
    public string id { get; set; }
    public string title { get; set; }
    public string[] nodes { get; set; }
}

[EditorAction("shader/focus", ShaderEditorCanvas.C_AREA)]
internal sealed class FocusShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    protected override bool needsSelection => false;
    protected override void Execute(EditorActionContext<AssetFileEntry> context) => documents.Open(context.target).frameRequested = true;
}

[EditorAction("shader/reroute", ShaderEditorCanvas.C_AREA)]
internal sealed class InsertShaderReroute(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool needsSelection => false;
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => base.Query(context).isEnabled && documents.Open(context.target).selectedEdge is not null ? EditorActionState.enabled : EditorActionState.disabled;
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        var controller = documents.Controller(draft);
        GraphDocument graph = controller.document.Clone();
        GraphEdgeRecord edge = graph.edges.Single(value => value.id == draft.selectedEdge);
        ShaderNodePort source = draft.ports[edge.output.nodeId].Single(value => value.id == edge.output.portId.value);
        var node = new GraphNodeRecord(new(Guid.NewGuid().ToString("N")), "inno.shader.reroute") { position = draft.menuPosition };
        node.SetValue("valueType", ShaderGraphDocument.Encode(ShaderGraphType.Capture(source.type), documents.serialization, documents.context));
        string stage = ShaderGraphDocument.Read(graph.FindNode(edge.output.nodeId)!, "stage", "", documents.serialization, documents.context);
        node.SetValue("stage", ShaderGraphDocument.Encode(stage, documents.serialization, documents.context));
        graph.AddNode(node);
        graph.RemoveEdge(edge.id);
        graph.AddEdge(new(new(Guid.NewGuid().ToString("N")), edge.output, new(node.id, new("input"))));
        graph.AddEdge(new(new(Guid.NewGuid().ToString("N")), new(node.id, new("value")), edge.input));
        controller.ReplaceDocument(graph, "Insert Shader Reroute");
        draft.selectedEdge = null;
        draft.canvas.SelectNodes([node.id]);
        documents.Changed(draft);
    }
}

[EditorAction("shader/group", ShaderEditorCanvas.C_AREA)]
internal sealed class GroupShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    internal const string C_GROUPS = "inno.editor.shader.groups";
    internal static ShaderCanvasGroup[] Read(ShaderEditorDocuments documents, GraphDocument graph)
        => graph.metadata.TryGetValue(C_GROUPS, out GraphSerializedValue? value)
            ? ShaderGraphDocument.Decode<ShaderCanvasGroup[]>(value, documents.serialization, documents.context) : [];
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        GraphDocument graph = documents.Controller(draft).document.Clone();
        ShaderCanvasGroup[] groups = Read(documents, graph);
        groups = [.. groups, new() { id = Guid.NewGuid().ToString("N"), title = "Group " + (groups.Length + 1),
            nodes = draft.canvas.selectedNodes.Select(static id => id.value).ToArray() }];
        graph.SetMetadata(C_GROUPS, ShaderGraphDocument.Encode(groups, documents.serialization, documents.context));
        documents.Controller(draft).ReplaceDocument(graph, "Group Shader Nodes");
        documents.Changed(draft);
    }
}

[EditorAction("shader/ungroup", ShaderEditorCanvas.C_AREA)]
internal sealed class UngroupShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        GraphDocument graph = documents.Controller(draft).document.Clone();
        var groups = GroupShaderNodes.Read(documents, graph).Where(group => !group.nodes.Any(id => draft.canvas.selectedNodes.Contains(new(id)))).ToArray();
        graph.SetMetadata(GroupShaderNodes.C_GROUPS, ShaderGraphDocument.Encode(groups, documents.serialization, documents.context));
        documents.Controller(draft).ReplaceDocument(graph, "Ungroup Shader Nodes");
        documents.Changed(draft);
    }
}

[EditorAction("shader/open-source", ShaderEditorCanvas.C_AREA)]
internal sealed class OpenShaderFunction(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (!base.Query(context).isEnabled) return EditorActionState.disabled;
        var draft = documents.Open(context.target);
        return draft.canvas.selectedNodes.Count == 1 && documents.Controller(draft).document.FindNode(draft.canvas.selectedNodes.First())?.definitionId == "inno.shader.source"
            ? EditorActionState.enabled : EditorActionState.disabled;
    }
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        GraphNodeRecord node = documents.Controller(draft).document.FindNode(draft.canvas.selectedNodes.First())!;
        Open(documents, ShaderGraphDocument.Read(node, "sourceId", Guid.Empty, documents.serialization, documents.context));
    }
    internal static void Open(ShaderEditorDocuments documents, Guid sourceId)
    {
        if (!documents.assets.TryGetInfo(sourceId, out AssetInfo? info) || info is null) throw new IOException("Source function is missing. Select a replacement in the node.");
        string path = documents.assets.sourceMounts.Single(mount => mount.id == info.assetPath.source).Resolve(info.assetPath.localPath);
        if (!File.Exists(path)) throw new FileNotFoundException("Source function is missing.", path);
        try { using Process? process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception failure) { throw new IOException("No source editor could open this file. Configure its application association.", failure); }
    }
}

[EditorAction("shader/diagnostics", ShaderEditorCanvas.C_AREA)]
internal sealed class ShowShaderDiagnostics(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    protected override bool needsSelection => false;
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
        => documents.Open(context.target).showDiagnostics = true;
}

[EditorAction("shader/copy-to-project", ShaderEditorCanvas.C_AREA)]
internal sealed class CopyShaderToProject(ShaderEditorDocuments documents, AssetEditorModule browser) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    protected override bool needsSelection => false;
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        AssetSourceMount mount = documents.assets.sourceMounts.Single(value => value.id == AssetSourceId.project);
        string name = Path.GetFileNameWithoutExtension(draft.path.localPath) + " Copy";
        AssetPath destination = AssetPath.Project(name + ".ishader");
        for (int index = 2; File.Exists(mount.Resolve(destination.localPath)) || File.Exists(mount.Resolve(destination.localPath) + ".imeta"); index++)
            destination = AssetPath.Project(name + " " + index + ".ishader");
        AssetFileEntry entry = browser.CreateSource(destination, GraphDocumentCodec.Encode(documents.Controller(draft).document, documents.serialization));
        context.interactions.SetSelection(entry);
        context.interactions.OpenPanel("rendering.shader-editor");
    }
}
