using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Graphs;
using Inno.Core.Input;
using Inno.Editor.Interactions;
using Inno.Editor.Shaders;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

[EditorMenuSource(ShaderEditorCanvas.C_AREA)]
internal sealed class ShaderCanvasMenu(ShaderEditorDocuments documents) : EditorMenuSource
{
    /// <inheritdoc />
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        if (context.target is not AssetFileEntry entry || !documents.TryGet(entry, out var draft)) return;
        builder.AddGroup("Create/Functions", order: 500, separatorBefore: true);
        builder.AddGroup("Create/Domain Outputs", order: 800, separatorBefore: true);
        bool explicitStages = ShaderGraphDocument.ReadTarget(documents.Controller(draft).document, documents.serialization, documents.context).Length == 0;
        if (draft.createFromPort is null && explicitStages)
        {
            builder.Add("Create/Outputs/Vertex Output", "shader/create-output", order: 0, argument: "Vertex");
            builder.Add("Create/Outputs/Fragment Output", "shader/create-output", order: 10, argument: "Fragment");
            builder.Add("Create/Outputs/Compute Output", "shader/create-output", order: 20, argument: "Compute");
        }
        foreach (string definition in documents.nodes.definitionIds)
            if (definition != "inno.shader.source" && documents.CanCreate(draft, new(definition)))
            {
                string category = Category(definition);
                int order = CategoryOrder(category);
                if (documents.drawers?.TryGetPresentation(definition, out ShaderNodePresentation presentation) == true
                    && presentation.createPath.Length != 0)
                {
                    category = presentation.createPath;
                    order = presentation.createOrder;
                    builder.AddGroup("Create/" + category, order, presentation.separatorBefore);
                }
                builder.Add("Create/" + category + "/" + (documents.drawers?.GetDisplayName(definition)
                    ?? Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget.NicifyName(definition.Replace("inno.shader.", "", StringComparison.Ordinal).Replace('-', ' '))),
                    "shader/create-node", order, argument: new ShaderNodeCreation(definition));
            }
        var functionLibraries = new List<(AssetFileEntry source, AssetInfo info, ShaderFunctionAsset library)>();
        foreach (AssetFileEntry source in documents.assets.GetFileSystemEntries(includeDirectories: false))
            if (source.extension == ".ishadersource" && documents.assets.TryGetInfo(source.assetPath, out var info) && info is not null
                && documents.assets.TryLoad(info.persistentId, out ShaderFunctionAsset? library) && library is not null && !library.isMissing)
                functionLibraries.Add((source, info, library));
        foreach (var catalog in functionLibraries
                     .Select(static value => (path: NormalizeCatalog(value.library.catalogPath), value.library.catalogOrder))
                     .Where(static value => value.path.Length != 0)
                     .GroupBy(static value => value.path.Split('/')[0], StringComparer.Ordinal)
                     .Select(static group => (name: group.Key, order: group.Min(static value => value.catalogOrder)))
                     .OrderBy(static value => value.order).ThenBy(static value => value.name, StringComparer.Ordinal))
            builder.AddGroup("Create/Functions/" + catalog.name, order: 500 + catalog.order, separatorBefore: true);
        foreach ((AssetFileEntry source, AssetInfo info, ShaderFunctionAsset library) in functionLibraries
                     .OrderBy(static value => value.library.catalogOrder)
                     .ThenBy(static value => value.source.nameWithoutExtension, StringComparer.Ordinal))
            foreach (string function in library.exports)
                    if (documents.CanCreate(draft, new("inno.shader.source", info.persistentId, function)))
                    {
                        string catalog = NormalizeCatalog(library.catalogPath);
                        string prefix = "Create/Functions/" + (catalog.Length == 0 ? "General" : catalog);
                        builder.Add(prefix + "/" + source.nameWithoutExtension + "/" + function, "shader/create-node",
                            order: 500 + library.catalogOrder,
                            argument: new ShaderNodeCreation("inno.shader.source", info.persistentId, function));
                    }
        builder.Add("View/Focus Selection", "shader/focus", order: 900);
        builder.Add("Edit/Copy", "shader/copy", order: 1000);
        builder.Add("Edit/Cut", "shader/cut", order: 110);
        builder.Add("Edit/Paste", "shader/paste", order: 120);
        builder.Add("Edit/Duplicate", "shader/duplicate", order: 130);
        builder.Add("Edit/Delete", "shader/delete", order: 140, separatorBefore: true);
        builder.Add("Connections/Disconnect", "shader/disconnect", order: 1500);
        builder.Add("Connections/Insert Reroute", "shader/reroute", order: 160);
        builder.Add("Organize/Group Selection", "shader/group", order: 1700);
        builder.Add("Organize/Ungroup Selection", "shader/ungroup", order: 180);
        builder.Add("Assets/Show Shader in File Browser", "shader/reveal-shader", order: 1900);
        builder.Add("Assets/Show Source in File Browser", "shader/reveal-source", order: 1910);
        builder.Add("Assets/Copy Shader To Project", "shader/copy-to-project", order: 1920, separatorBefore: true);

        static string Category(string definition) => definition switch
        {
            "inno.shader.constant" or "inno.shader.stage-input" => "Inputs",
            "inno.shader.sample" => "Textures",
            "inno.shader.binary" or "inno.shader.construct" or "inno.shader.extract" or "inno.shader.select" => "Math",
            "inno.shader.storage-load" or "inno.shader.storage-store" or "inno.shader.storage-atomic-add" => "Resources",
            "inno.shader.discard" => "Flow",
            "inno.shader.reroute" => "Utility",
            _ when definition.Contains("surface-output", StringComparison.Ordinal) => "Domain Outputs",
            _ => "Domain"
        };

        static int CategoryOrder(string category) => category switch
        {
            "Outputs" => 0,
            "Inputs" => 100,
            "Textures" => 200,
            "Math" => 300,
            "Resources" => 400,
            "Flow" => 600,
            "Utility" => 700,
            "Domain Outputs" => 800,
            _ => 850
        };

        static string NormalizeCatalog(string value)
            => string.Join('/', value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

[EditorAction("shader/create-output", ShaderEditorCanvas.C_AREA)]
internal sealed class CreateShaderOutput(ShaderEditorDocuments documents) : EditorAction<AssetFileEntry, string>
{
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry, string> context)
    {
        if (!documents.TryGet(context.target, out var draft) || draft.readOnly
            || !Enum.TryParse(context.argument, out Inno.Rendering.ShaderStage stage)) return EditorActionState.disabled;
        GraphDocument graph = documents.Controller(draft).document;
        if (ShaderGraphDocument.ReadTarget(graph, documents.serialization, documents.context).Length != 0)
            return EditorActionState.disabled;
        bool exists = graph.nodes.Where(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .Any(node => ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), documents.serialization, documents.context).stage == stage);
        return exists ? EditorActionState.disabled : EditorActionState.enabled;
    }
    protected override void Execute(EditorActionContext<AssetFileEntry, string> context)
    {
        var draft = documents.Open(context.target);
        var controller = documents.Controller(draft);
        GraphDocument graph = controller.document.Clone();
        Inno.Rendering.ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, documents.serialization, documents.context);
        Inno.Rendering.ShaderStage stage = Enum.Parse<Inno.Rendering.ShaderStage>(context.argument);
        Inno.Rendering.ShaderProgramKind kind = stage == Inno.Rendering.ShaderStage.Compute
            ? Inno.Rendering.ShaderProgramKind.Compute : Inno.Rendering.ShaderProgramKind.Raster;
        ShaderGraphPassProgram[] programs = ShaderGraphPrograms.Read(graph, documents.serialization, documents.context);
        ShaderGraphPassProgram? incomplete = kind == Inno.Rendering.ShaderProgramKind.Raster
            ? programs.FirstOrDefault(program => definition.passes.Any(pass => pass.name == program.pass && pass.programKind == kind)
                && !program.stages.Select(id => graph.FindNode(new(id))).OfType<GraphNodeRecord>()
                    .Any(node => ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), documents.serialization, documents.context).stage == stage))
            : null;
        string name;
        string[] stages;
        if (incomplete is ShaderGraphPassProgram existing && existing.pass is not null)
        { name = existing.pass; stages = existing.stages; }
        else
        {
            string prefix = kind.ToString();
            name = prefix;
            for (int suffix = 2; definition.passes.Any(pass => pass.name == name); suffix++) name = prefix + " " + suffix;
            definition.passes = [.. definition.passes, new(name, kind)];
            graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(documents.serialization.Serialize(definition, documents.context), documents.serialization, documents.context));
            stages = [];
        }
        GraphNodeId id = new(Guid.NewGuid().ToString("N"));
        var node = new GraphNodeRecord(id, ShaderGraphDocument.outputDefinitionId) { position = draft.menuPosition };
        node.SetValue("settings", ShaderGraphDocument.Encode(new ShaderGraphStageSettings { stage = stage,
            outputs = stage == Inno.Rendering.ShaderStage.Compute ? [] : [new() { id = stage == Inno.Rendering.ShaderStage.Vertex ? "position" : "color",
                kind = stage == Inno.Rendering.ShaderStage.Vertex ? ShaderIrOutputKind.ClipPosition : ShaderIrOutputKind.Color }] }, documents.serialization, documents.context));
        graph.AddNode(node);
        graph = ShaderGraphPrograms.Bind(graph, name, stages.Select(static value => new GraphNodeId(value)).Append(id), documents.serialization, documents.context);
        controller.ReplaceDocument(graph, "Create " + stage + " Output");
        draft.canvas.SelectNodes([id]);
        draft.activeStage = id;
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
        => base.Query(context).isEnabled && documents.clipboard is not null
            && documents.TryGet(context.target, out var draft) && documents.CanPaste(draft, documents.clipboard)
            ? EditorActionState.enabled : EditorActionState.disabled;
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
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
    {
        if (!base.Query(context).isEnabled || !documents.TryGet(context.target, out var draft)) return EditorActionState.disabled;
        return draft.canvas.selectedNodes.Any(id => documents.Controller(draft).document.FindNode(id)?.definitionId == ShaderGraphDocument.outputDefinitionId)
            ? EditorActionState.disabled : EditorActionState.enabled;
    }
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

[EditorAction("shader/format", ShaderEditorCanvas.C_AREA)]
internal sealed class FormatShaderGraph(ShaderEditorDocuments documents) : ShaderSelectionAction(documents)
{
    protected override bool needsSelection => false;
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        var draft = documents.Open(context.target);
        var controller = documents.Controller(draft);
        controller.ReplaceDocument(ShaderGraphAutoLayout.Apply(
            controller.document,
            documents.serialization,
            documents.context,
            documents.Describe), "Format Shader Graph");
        draft.frameRequested = true;
        documents.Changed(draft);
    }
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
