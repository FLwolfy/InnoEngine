using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.Input;
using Inno.Editor.Interactions;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

[EditorMenuSource(ShaderEditorCanvas.C_AREA)]
internal sealed class ShaderCanvasMenu(ShaderEditorDocuments documents) : EditorMenuSource
{
    /// <inheritdoc />
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        if (context.target is not AssetFileEntry entry || !documents.TryGet(entry, out var draft)) return;
        if (draft.createFromPort is null)
        {
            builder.Add("Create/Raster Pass", "shader/create-pass", argument: "Raster");
            builder.Add("Create/Compute Pass", "shader/create-pass", argument: "Compute");
        }
        foreach (string definition in documents.nodes.definitionIds)
            if (documents.CanCreate(draft, new(definition)))
                builder.Add("Create/" + (documents.drawers?.GetDisplayName(definition)
                    ?? Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.NicifyName(definition.Replace("inno.shader.", "", StringComparison.Ordinal).Replace('-', ' '))),
                    "shader/create-node", argument: new ShaderNodeCreation(definition));
        foreach (AssetFileEntry source in documents.assets.GetFileSystemEntries(includeDirectories: false))
            if (source.extension == ".ishadersource" && documents.assets.TryGetInfo(source.assetPath, out var info) && info is not null
                && documents.CanCreate(draft, new("inno.shader.source", info.persistentId)))
                builder.Add("Create/Source Functions/" + source.nameWithoutExtension, "shader/create-node",
                    argument: new ShaderNodeCreation("inno.shader.source", info.persistentId));
        builder.Add("Focus", "shader/focus", order: 90, separatorBefore: true);
        builder.Add("Copy", "shader/copy", order: 100, separatorBefore: true);
        builder.Add("Cut", "shader/cut", order: 110);
        builder.Add("Paste", "shader/paste", order: 120);
        builder.Add("Duplicate", "shader/duplicate", order: 130);
        builder.Add("Disconnect", "shader/disconnect", order: 140);
        builder.Add("Delete", "shader/delete", order: 150);
        builder.Add("Insert Reroute", "shader/reroute", order: 160);
        builder.Add("Group Selection", "shader/group", order: 170);
        builder.Add("Ungroup Selection", "shader/ungroup", order: 180);
        builder.Add("Open Source", "shader/open-source", order: 190);
        builder.Add("Compilation Diagnostics", "shader/diagnostics", order: 195);
        builder.Add("Save", "shader/save", order: 200, separatorBefore: true);
        builder.Add("Revert to Saved", "shader/reload", order: 210);
        builder.Add("Copy Shader To Project", "shader/copy-to-project", order: 220);
    }
}

[EditorAction("shader/create-pass", ShaderEditorCanvas.C_AREA)]
internal sealed class CreateShaderPass(ShaderEditorDocuments documents) : EditorAction<AssetFileEntry, string>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry, string> context)
        => documents.TryGet(context.target, out var draft) && !draft.readOnly ? EditorActionState.enabled : EditorActionState.disabled;
    protected override void Execute(EditorActionContext<AssetFileEntry, string> context)
    {
        var draft = documents.Open(context.target);
        var controller = documents.Controller(draft);
        GraphDocument graph = controller.document.Clone();
        Inno.Rendering.ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, documents.serialization, documents.context);
        Inno.Rendering.ShaderProgramKind kind = Enum.Parse<Inno.Rendering.ShaderProgramKind>(context.argument);
        string name = kind.ToString();
        for (int suffix = 2; definition.passes.Any(pass => pass.name == name); suffix++) name = kind + " " + suffix;
        definition.passes = [.. definition.passes, new(name, kind)];
        graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(documents.serialization.Serialize(definition, documents.context), documents.serialization, documents.context));
        Inno.Rendering.ShaderStage[] stages = kind == Inno.Rendering.ShaderProgramKind.Compute ? [Inno.Rendering.ShaderStage.Compute] : [Inno.Rendering.ShaderStage.Vertex, Inno.Rendering.ShaderStage.Fragment];
        var created = new List<GraphNodeId>();
        foreach (Inno.Rendering.ShaderStage stage in stages)
        {
            GraphNodeId id = new(Guid.NewGuid().ToString("N"));
            var node = new GraphNodeRecord(id, ShaderGraphDocument.outputDefinitionId) { position = new(draft.menuPosition.x, draft.menuPosition.y + created.Count * 340) };
            node.SetValue("settings", ShaderGraphDocument.Encode(new ShaderGraphStageSettings { stage = stage,
                outputs = stage == Inno.Rendering.ShaderStage.Compute ? [] : [new() { id = stage == Inno.Rendering.ShaderStage.Vertex ? "position" : "color",
                    kind = stage == Inno.Rendering.ShaderStage.Vertex ? ShaderIrOutputKind.ClipPosition : ShaderIrOutputKind.Color }] }, documents.serialization, documents.context));
            graph.AddNode(node);
            created.Add(id);
        }
        graph = ShaderGraphPrograms.Bind(graph, name, created, documents.serialization, documents.context);
        controller.ReplaceDocument(graph, "Create Shader Pass");
        draft.canvas.SelectNodes(created);
        draft.activeStage = created[0];
        documents.Changed(draft);
    }
}

[EditorAction("shader/create-node", ShaderEditorCanvas.C_AREA)]
internal sealed class CreateShaderNode(ShaderEditorDocuments documents) : EditorAction<AssetFileEntry, ShaderNodeCreation>
{
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry, ShaderNodeCreation> context)
        => documents.TryGet(context.target, out var draft) && documents.CanCreate(draft, context.argument) ? EditorActionState.enabled : EditorActionState.disabled;
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry, ShaderNodeCreation> context)
        => documents.Create(documents.Open(context.target), context.argument);
}

internal abstract class ShaderSelectionAction(ShaderEditorDocuments documents) : EditorAction<AssetFileEntry>
{
    protected ShaderEditorDocuments documents { get; } = documents;
    protected virtual bool writes => true;
    protected virtual bool needsSelection => true;
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => documents.TryGet(context.target, out var draft)
            && (!writes || !draft.readOnly) && (!needsSelection || draft.canvas.selectedNodes.Count != 0)
            ? EditorActionState.enabled : EditorActionState.disabled;
}

[EditorAction("shader/copy", ShaderEditorCanvas.C_AREA)]
[EditorShortcut(ShaderEditorCanvas.C_AREA, KeyCode.C, primary: true)]
internal sealed class CopyShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool writes => false;
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        documents.clipboard = documents.Copy(draft);
    }
}

[EditorAction("shader/delete", ShaderEditorCanvas.C_AREA)]
[EditorShortcut(ShaderEditorCanvas.C_AREA, KeyCode.Delete)]
[EditorShortcut(ShaderEditorCanvas.C_AREA, KeyCode.Backspace)]
internal sealed class DeleteShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool needsSelection => false;
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        var controller = documents.Controller(draft);
        using var transaction = context.history.BeginTransaction("Delete Shader Selection");
        if (draft.selectedEdge is GraphEdgeId edge) controller.Disconnect(edge);
        documents.RemoveNodes(draft);
        transaction.Commit();
        draft.selectedEdge = null;
        draft.canvas.ClearSelection();
        documents.Changed(draft);
    }
}

[EditorAction("shader/cut", ShaderEditorCanvas.C_AREA)]
[EditorShortcut(ShaderEditorCanvas.C_AREA, KeyCode.X, primary: true)]
internal sealed class CutShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        documents.clipboard = documents.Copy(draft);
        documents.RemoveNodes(draft);
        draft.canvas.ClearSelection();
        documents.Changed(draft);
    }
}

[EditorAction("shader/paste", ShaderEditorCanvas.C_AREA)]
[EditorShortcut(ShaderEditorCanvas.C_AREA, KeyCode.V, primary: true)]
internal sealed class PasteShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool needsSelection => false;
    /// <inheritdoc />
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => base.Query(context).isEnabled && documents.clipboard is not null ? EditorActionState.enabled : EditorActionState.disabled;
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        if (documents.clipboard is not null) documents.Paste(draft, documents.clipboard);
    }
}

[EditorAction("shader/duplicate", ShaderEditorCanvas.C_AREA)]
[EditorShortcut(ShaderEditorCanvas.C_AREA, KeyCode.D, primary: true)]
internal sealed class DuplicateShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        documents.Paste(draft, documents.Copy(draft));
    }
}

[EditorAction("shader/disconnect", ShaderEditorCanvas.C_AREA)]
internal sealed class DisconnectShaderNodes(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        var controller = documents.Controller(draft);
        using var transaction = context.history.BeginTransaction("Disconnect Nodes");
        foreach (GraphEdgeRecord edge in controller.document.edges.Where(edge => draft.canvas.selectedNodes.Contains(edge.input.nodeId) || draft.canvas.selectedNodes.Contains(edge.output.nodeId)).ToArray())
            controller.Disconnect(edge.id);
        transaction.Commit();
        documents.Changed(draft);
    }
}

[EditorAction("shader/save", ShaderEditorCanvas.C_AREA)]
[EditorShortcut(ShaderEditorCanvas.C_AREA, KeyCode.S, primary: true)]
internal sealed class SaveShader(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool needsSelection => false;
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
        => context.interactions.documents.Save(documents.Open(context.target).documentId);
}

[EditorAction("shader/reload", ShaderEditorCanvas.C_AREA)]
internal sealed class ReloadShaderSource(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool needsSelection => false;
    protected override bool writes => false;
    /// <inheritdoc />
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
        => context.interactions.documents.Revert(documents.Open(context.target).documentId);
}
