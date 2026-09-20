using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Editor.Graph;
using Inno.Editor.Interactions;
using Inno.Editor.Panel.FileBrowser;
using Inno.Rendering;
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
        draft.selectedGroupId = groups[^1].id;
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
        var groups = GroupShaderNodes.Read(documents, graph).Where(group => draft.selectedGroupId.Length != 0
            ? group.id != draft.selectedGroupId
            : !group.nodes.All(id => draft.canvas.selectedNodes.Contains(new(id)))).ToArray();
        graph.SetMetadata(GroupShaderNodes.C_GROUPS, ShaderGraphDocument.Encode(groups, documents.serialization, documents.context));
        documents.Controller(draft).ReplaceDocument(graph, "Ungroup Shader Nodes");
        draft.selectedGroupId = "";
        documents.Changed(draft);
    }
}

[EditorAction("shader/collapse-subgraph", ShaderEditorCanvas.C_AREA)]
internal sealed class CollapseShaderSubgraph(ShaderEditorDocuments documents, AssetEditorModule browser)
    : ShaderSelectionAction(documents)
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (!base.Query(context).isEnabled) return EditorActionState.disabled;
        ShaderEditorDocuments.Draft draft = documents.Open(context.target);
        GraphDocument graph = documents.Controller(draft).document;
        GraphNodeRecord[] selected = draft.canvas.selectedNodes.Select(graph.FindNode).OfType<GraphNodeRecord>().ToArray();
        if (selected.Length != draft.canvas.selectedNodes.Count || selected.Any(static node =>
                node.definitionId is ShaderGraphDocument.outputDefinitionId
                    or ShaderGraphNodes.inputDefinitionId
                    or ShaderGraphNodes.outputDefinitionId))
            return EditorActionState.disabled;
        string[] stages = selected.Select(node => ShaderGraphDocument.Read(node, ShaderGraphDocument.stageKey,
                "", documents.serialization, documents.context))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (stages.Length != 1 || stages[0].Length == 0) return EditorActionState.disabled;
        HashSet<GraphNodeId> ids = selected.Select(static node => node.id).ToHashSet();
        return graph.edges.Any(edge => ids.Contains(edge.input.nodeId) != ids.Contains(edge.output.nodeId))
            ? EditorActionState.enabled
            : EditorActionState.disabled;
    }

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        ShaderEditorDocuments.Draft draft = documents.Open(context.target);
        GraphDocumentController controller = documents.Controller(draft);
        GraphDocument parent = controller.document;
        HashSet<GraphNodeId> selected = [.. draft.canvas.selectedNodes];
        GraphNodeRecord[] nodes = parent.nodes.Where(node => selected.Contains(node.id)).ToArray();
        string stage = nodes.Select(node => ShaderGraphDocument.Read(node, ShaderGraphDocument.stageKey,
                "", documents.serialization, documents.context))
            .Distinct(StringComparer.Ordinal).Single();
        GraphEdgeRecord[] incomingEdges = parent.edges
            .Where(edge => selected.Contains(edge.input.nodeId) && !selected.Contains(edge.output.nodeId)).ToArray();
        GraphEdgeRecord[] outgoingEdges = parent.edges
            .Where(edge => selected.Contains(edge.output.nodeId) && !selected.Contains(edge.input.nodeId)).ToArray();

        var inputGroups = incomingEdges.GroupBy(static edge => edge.output).ToArray();
        var outputGroups = outgoingEdges.GroupBy(static edge => edge.output).ToArray();
        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        ShaderGraphNodePortDefinition[] inputs = inputGroups.Select(group => Port(
            Unique(group.First().input.portId.value, "input", inputNames),
            draft.ports[group.First().input.nodeId].Single(port => port.id == group.First().input.portId.value).type,
            required: true)).ToArray();
        ShaderGraphNodePortDefinition[] outputs = outputGroups.Select(group => Port(
            Unique(group.Key.portId.value, "output", outputNames),
            draft.ports[group.Key.nodeId].Single(port => port.id == group.Key.portId.value).type,
            required: false)).ToArray();

        ShaderDefinition parentDefinition = ShaderGraphDocument.ReadDefinition(parent, documents.serialization, documents.context);
        HashSet<string> ownedBindings = nodes.Where(static node => node.definitionId == "inno.shader.stage-input")
            .Select(node => ShaderGraphDocument.Read(node, ShaderGraphDocument.settingsKey,
                new ShaderGraphInputSettings(), documents.serialization, documents.context).id)
            .ToHashSet(StringComparer.Ordinal);
        ShaderDefinition childDefinition = new(
            "Graph Node",
            parentDefinition.properties.Where(property => ownedBindings.Contains(property.id.value)),
            [],
            []);
        GraphDocument child = ShaderGraphDocument.Create(childDefinition, documents.serialization, documents.context);
        string displayName = SuggestedName(documents, draft, selected);
        var nodeSettings = new ShaderGraphNodeSettings
        {
            displayName = displayName,
            createPath = "Project",
            kind = ShaderGraphNodeKind.Function,
            effect = outputs.Length == 0 ? ShaderGraphNodeEffect.SideEffect : ShaderGraphNodeEffect.Pure
        };
        ShaderGraphNodes.WriteSettings(child, nodeSettings, documents.serialization, documents.context);

        float minX = nodes.Min(static node => node.position.x);
        float minY = nodes.Min(static node => node.position.y);
        var remap = new Dictionary<GraphNodeId, GraphNodeId>();
        foreach (GraphNodeRecord source in nodes)
        {
            var id = new GraphNodeId("body/" + source.id.value);
            var copy = new GraphNodeRecord(id, source.definitionId)
            {
                position = new(source.position.x - minX + 280, source.position.y - minY + 80)
            };
            foreach ((string key, GraphSerializedValue value) in source.values)
                if (key != ShaderGraphDocument.stageKey) copy.SetValue(key, value.Clone());
            child.AddNode(copy);
            remap.Add(source.id, id);
        }
        foreach (GraphEdgeRecord edge in parent.edges.Where(edge => selected.Contains(edge.output.nodeId)
                     && selected.Contains(edge.input.nodeId)))
            child.AddEdge(new(
                new("body/" + edge.id.value),
                new(remap[edge.output.nodeId], edge.output.portId),
                new(remap[edge.input.nodeId], edge.input.portId)));

        if (inputs.Length != 0)
        {
            var boundary = new GraphNodeRecord(new("function-inputs"), ShaderGraphNodes.inputDefinitionId)
            {
                position = new(20, 80)
            };
            boundary.SetValue(ShaderGraphDocument.settingsKey, ShaderGraphDocument.Encode(
                new ShaderGraphNodeInputSettings { ports = inputs }, documents.serialization, documents.context));
            child.AddNode(boundary);
            for (int index = 0; index < inputGroups.Length; index++)
                foreach (GraphEdgeRecord edge in inputGroups[index])
                    child.AddEdge(new(
                        new("input/" + edge.id.value),
                        new(boundary.id, new(inputs[index].id)),
                        new(remap[edge.input.nodeId], edge.input.portId)));
        }
        if (outputs.Length != 0)
        {
            var boundary = new GraphNodeRecord(new("function-outputs"), ShaderGraphNodes.outputDefinitionId)
            {
                position = new(nodes.Max(static node => node.position.x) - minX + 600, 80)
            };
            boundary.SetValue(ShaderGraphDocument.settingsKey, ShaderGraphDocument.Encode(
                new ShaderGraphNodeOutputSettings { ports = outputs }, documents.serialization, documents.context));
            child.AddNode(boundary);
            for (int index = 0; index < outputGroups.Length; index++)
                child.AddEdge(new(
                    new("output/" + index),
                    new(remap[outputGroups[index].Key.nodeId], outputGroups[index].Key.portId),
                    new(boundary.id, new(outputs[index].id))));
        }

        AssetPath path = UniquePath(draft.path, displayName, documents);
        using EditorHistoryTransaction transaction = documents.interactions.history.BeginTransaction("Collapse Shader Subgraph");
        AssetFileEntry created = browser.CreateSource(path, GraphDocumentCodec.Encode(child, documents.serialization));
        Guid sourceId = documents.AssetId(created);
        ShaderGraphNodeInterface nodeInterface = ShaderGraphNodes.ReadInterface(
            child, documents.serialization, documents.context);
        GraphDocument candidate = parent.Clone();
        foreach (GraphNodeId id in selected) candidate.RemoveNode(id);
        var call = new GraphNodeRecord(new(Guid.NewGuid().ToString("N")), ShaderGraphNodes.callDefinitionId)
        {
            position = new(nodes.Average(static node => node.position.x), nodes.Average(static node => node.position.y))
        };
        call.SetValue(ShaderGraphDocument.stageKey, ShaderGraphDocument.Encode(stage, documents.serialization, documents.context));
        call.SetValue("sourceId", ShaderGraphDocument.Encode(sourceId, documents.serialization, documents.context));
        call.SetValue("sourcePath", ShaderGraphDocument.Encode(path.ToString(), documents.serialization, documents.context));
        call.SetValue(ShaderGraphNodes.interfaceKey, ShaderGraphDocument.Encode(nodeInterface, documents.serialization, documents.context));
        candidate.AddNode(call);
        for (int index = 0; index < inputGroups.Length; index++)
            candidate.AddEdge(new(
                new(Guid.NewGuid().ToString("N")),
                inputGroups[index].Key,
                new(call.id, new(inputs[index].id))));
        for (int index = 0; index < outputGroups.Length; index++)
            foreach (GraphEdgeRecord edge in outputGroups[index])
                candidate.AddEdge(new(
                    new(Guid.NewGuid().ToString("N")),
                    new(call.id, new(outputs[index].id)),
                    edge.input));

        ShaderCanvasGroup[] groups = GroupShaderNodes.Read(documents, candidate).Select(group =>
        {
            group.nodes = group.nodes.Where(id => !selected.Contains(new(id))).ToArray();
            return group;
        }).Where(static group => group.nodes.Length != 0).ToArray();
        candidate.SetMetadata(GroupShaderNodes.C_GROUPS,
            ShaderGraphDocument.Encode(groups, documents.serialization, documents.context));
        controller.ReplaceDocument(candidate, "Collapse Shader Subgraph");
        transaction.Commit();
        draft.canvas.SelectNodes([call.id]);
        draft.selectedGroupId = "";
        documents.Changed(draft);
    }

    private static ShaderGraphNodePortDefinition Port(string id, ShaderSourceType type, bool required)
        => new() { id = id, type = ShaderGraphType.Capture(type), required = required };

    private static string Unique(string candidate, string fallback, HashSet<string> used)
    {
        string root = string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
        string value = root;
        for (int suffix = 2; !used.Add(value); suffix++) value = root + suffix;
        return value;
    }

    private static string SuggestedName(ShaderEditorDocuments documents, ShaderEditorDocuments.Draft draft,
        HashSet<GraphNodeId> selected)
    {
        ShaderCanvasGroup? group = GroupShaderNodes.Read(documents, documents.Controller(draft).document)
            .FirstOrDefault(value => value.nodes.Length == selected.Count
                && value.nodes.All(id => selected.Contains(new(id))));
        return group is ShaderCanvasGroup exact && !string.IsNullOrWhiteSpace(exact.title)
            ? exact.title
            : "Shader Node";
    }

    private static AssetPath UniquePath(AssetPath source, string name, ShaderEditorDocuments documents)
    {
        AssetSourceMount mount = documents.assets.sourceMounts.Single(value => value.id == source.source);
        string directory = Path.GetDirectoryName(source.localPath)?.Replace('\\', '/') ?? "";
        string safeName = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        if (safeName.Length == 0) safeName = "Shader Node";
        string prefix = directory.Length == 0 ? "" : directory + "/";
        AssetPath path = new(source.source, prefix + safeName + ".ishader");
        for (int suffix = 2; File.Exists(mount.Resolve(path.localPath)) || File.Exists(mount.Resolve(path.localPath) + ".imeta"); suffix++)
            path = new(source.source, prefix + safeName + " " + suffix + ".ishader");
        return path;
    }
}

[EditorAction("shader/reveal-source", ShaderEditorCanvas.C_AREA)]
internal sealed class RevealShaderFunction(ShaderEditorDocuments documents, AssetEditorModule browser) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (!base.Query(context).isEnabled) return EditorActionState.disabled;
        var draft = documents.Open(context.target);
        string? definition = draft.canvas.selectedNodes.Count == 1
            ? documents.Controller(draft).document.FindNode(draft.canvas.selectedNodes.First())?.definitionId
            : null;
        return definition is "inno.shader.source" or ShaderGraphNodes.callDefinitionId
            ? EditorActionState.enabled
            : EditorActionState.disabled;
    }
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        GraphNodeRecord node = documents.Controller(draft).document.FindNode(draft.canvas.selectedNodes.First())!;
        Reveal(documents, browser, ShaderGraphDocument.Read(node, "sourceId", Guid.Empty, documents.serialization, documents.context));
    }
    internal static void Reveal(ShaderEditorDocuments documents, AssetEditorModule browser, Guid sourceId)
    {
        if (!documents.assets.TryGetInfo(sourceId, out AssetInfo? info) || info is null) throw new IOException("Referenced Shader authoring asset is missing. Select a replacement in the node.");
        if (!documents.assets.TryGetFileSystemEntry(info.assetPath, out AssetFileEntry entry))
            throw new FileNotFoundException("Referenced Shader authoring asset is missing from the Asset Browser.", info.assetPath.ToString());
        string parent = Path.GetDirectoryName(info.assetPath.localPath)?.Replace('\\', '/') ?? string.Empty;
        browser.browser.SetCurrentDirectory(new AssetPath(info.assetPath.source, parent).ToString());
        documents.interactions.SetSelection(entry);
        documents.interactions.OpenPanel("asset.file-browser");
    }
}

[EditorAction("shader/reveal-shader", ShaderEditorCanvas.C_AREA)]
internal sealed class RevealShaderAsset(ShaderEditorDocuments documents, AssetEditorModule browser) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    protected override bool needsSelection => false;
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        string parent = Path.GetDirectoryName(draft.path.localPath)?.Replace('\\', '/') ?? string.Empty;
        browser.browser.SetCurrentDirectory(new AssetPath(draft.path.source, parent).ToString());
        if (!documents.assets.TryGetFileSystemEntry(draft.path, out AssetFileEntry entry))
            throw new FileNotFoundException("Shader source is missing from the Asset Browser.", draft.path.ToString());
        documents.interactions.SetSelection(entry);
        documents.interactions.OpenPanel("asset.file-browser");
    }
}

[EditorAction("shader/check", ShaderEditorCanvas.C_AREA)]
internal sealed class ShowShaderDiagnostics(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    protected override bool needsSelection => false;
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
        => documents.ShowCheck(documents.Open(context.target));
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
        browser.BeginCreatedSourceRename(entry);
        context.interactions.OpenPanel("rendering.shader-editor");
    }
}
