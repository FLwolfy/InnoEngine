using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Core.Graphs;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed record ShaderNodeCreation(string definitionId, Guid sourceId = default);

internal sealed partial class ShaderEditorDocuments
{
    internal GraphNodeRecord PrepareNode(Draft draft, ShaderNodeCreation creation)
    {
        GraphDocument graph = Controller(draft).document;
        GraphNodeId? stage = draft.createFromPort is GraphEndpoint endpoint
            ? new(ShaderGraphDocument.Read(graph.FindNode(endpoint.nodeId)!, "stage", "", serialization, context))
            : draft.activeStage ?? graph.nodes.FirstOrDefault(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)?.id;
        var node = new GraphNodeRecord(new(Guid.NewGuid().ToString("N")), creation.definitionId) { position = draft.menuPosition };
        if (stage is GraphNodeId id) node.SetValue("stage", ShaderGraphDocument.Encode(id.value, serialization, context));
        if (creation.definitionId == "inno.shader.stage-input")
        {
            HashSet<string> names = graph.nodes.Where(static value => value.definitionId == "inno.shader.stage-input")
                .Select(value => ShaderGraphDocument.Read(value, "settings", new ShaderGraphInputSettings(), serialization, context).id).ToHashSet(StringComparer.Ordinal);
            string name = "parameter";
            for (int suffix = 2; names.Contains(name); suffix++) name = "parameter" + suffix;
            node.SetValue("settings", ShaderGraphDocument.Encode(new ShaderGraphInputSettings
            { id = name, kind = ShaderIrInputKind.Uniform, type = new() { id = "float4" }, semantic = "" }, serialization, context));
        }
        if (draft.createFromPort is GraphEndpoint source)
        {
            ShaderNodePort port = draft.ports[source.nodeId].Single(value => value.id == source.portId.value);
            node.SetValue("type", ShaderGraphDocument.Encode(port.type.id, serialization, context));
            if (creation.definitionId == "inno.shader.reroute")
                node.SetValue("valueType", ShaderGraphDocument.Encode(ShaderGraphType.Capture(port.type), serialization, context));
        }
        if (creation.sourceId != Guid.Empty)
        {
            if (!assets.TryGetInfo(creation.sourceId, out AssetInfo? info) || info is null) throw new IOException("Source function is unavailable.");
            node.SetValue("sourceId", ShaderGraphDocument.Encode(creation.sourceId, serialization, context));
            node.SetValue("sourcePath", ShaderGraphDocument.Encode(info.assetPath.ToString(), serialization, context));
        }
        return node;
    }

    internal IReadOnlyList<ShaderNodePort> Describe(GraphNodeRecord node)
    {
        ShaderSourceModuleAnalysis? module = null;
        string implementation = "";
        ShaderIrStageInput? input = node.definitionId == "inno.shader.stage-input"
            ? ShaderGraphDocument.Read(node, "settings", new ShaderGraphInputSettings(), serialization, context).CreateBinding() : null;
        if (node.definitionId == "inno.shader.source")
        {
            Guid id = ShaderGraphDocument.Read(node, "sourceId", Guid.Empty, serialization, context);
            if (id != Guid.Empty && assets.TryGetInfo(id, out AssetInfo? info) && info is not null && info.status != AssetImportStatus.Imported)
                throw new InvalidOperationException("Source function " + info.status + ": " + string.Join("\n", info.diagnostics));
            if (id != Guid.Empty && assets.TryLoad(id, out ShaderFunctionAsset? function) && function is not null && !function.isMissing)
            {
                module = frontends.AnalyzeModule(ShaderSourceBundle.Decode(ShaderSourceBundle.Read(function, assets), serialization));
                implementation = function.implementationId;
            }
        }
        return nodes.DescribePorts(node, serialization, context, module, implementation, input);
    }

    internal ShaderNodePort? CompatibleInput(Draft draft, GraphNodeRecord node)
    {
        if (draft.createFromPort is not GraphEndpoint source) return null;
        ShaderNodePort output = draft.ports[source.nodeId].Single(value => value.id == source.portId.value);
        return Describe(node).FirstOrDefault(value => value.direction == GraphPortDirection.Input && value.type.IsEquivalentTo(output.type));
    }

    internal bool CanCreate(Draft draft, ShaderNodeCreation creation)
    {
        if (draft.readOnly) return false;
        if (draft.createFromPort is null) return true;
        try { return CompatibleInput(draft, PrepareNode(draft, creation)) is not null; }
        catch (Exception failure) when ((failure is InvalidOperationException or ArgumentException or IOException or FormatException) && Inno.Core.Execution.RetirementPendingException.Find(failure) is null) { return false; }
    }

    internal void Create(Draft draft, ShaderNodeCreation creation)
    {
        GraphNodeRecord node = PrepareNode(draft, creation);
        ShaderNodePort? input = CompatibleInput(draft, node);
        GraphDocument graph = Controller(draft).document.Clone();
        graph.AddNode(node);
        if (node.definitionId == "inno.shader.stage-input")
            graph = ShaderGraphBindings.ChangeInput(graph, node.id,
                ShaderGraphDocument.Read(node, "settings", new ShaderGraphInputSettings(), serialization, context), serialization, context);
        if (input is not null && draft.createFromPort is GraphEndpoint source)
            graph.AddEdge(new(new(Guid.NewGuid().ToString("N")), source, new(node.id, new(input.id))));
        Controller(draft).ReplaceDocument(graph, "Create Shader Node");
        draft.createFromPort = null;
        draft.canvas.SelectNodes([node.id]);
        Changed(draft);
    }
}
