using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Core.Graphs;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed record ShaderNodeCreation(string definitionId, Guid sourceId = default, string function = "");

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
        else if (creation.definitionId == ShaderGraphNodes.inputDefinitionId)
            node.SetValue(ShaderGraphDocument.settingsKey,
                ShaderGraphDocument.Encode(new ShaderGraphNodeInputSettings(), serialization, context));
        else if (creation.definitionId == ShaderGraphNodes.outputDefinitionId)
            node.SetValue(ShaderGraphDocument.settingsKey,
                ShaderGraphDocument.Encode(new ShaderGraphNodeOutputSettings(), serialization, context));
        if (draft.createFromPort is GraphEndpoint source)
        {
            ShaderNodePort port = draft.ports[source.nodeId].Single(value => value.id == source.portId.value);
            node.SetValue("type", ShaderGraphDocument.Encode(port.type.id, serialization, context));
            if (creation.definitionId == "inno.shader.reroute")
                node.SetValue("valueType", ShaderGraphDocument.Encode(ShaderGraphType.Capture(port.type), serialization, context));
        }
        if (creation.sourceId != Guid.Empty)
        {
            if (!assets.TryGetInfo(creation.sourceId, out AssetInfo? info) || info is null)
                throw new IOException("Referenced Shader authoring asset is unavailable.");
            node.SetValue("sourceId", ShaderGraphDocument.Encode(creation.sourceId, serialization, context));
            node.SetValue("sourcePath", ShaderGraphDocument.Encode(info.assetPath.ToString(), serialization, context));
            if (creation.definitionId == "inno.shader.source")
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(creation.function);
                node.SetValue("function", ShaderGraphDocument.Encode(creation.function, serialization, context));
            }
            else if (creation.definitionId == ShaderGraphNodes.callDefinitionId)
                node.SetValue(ShaderGraphNodes.interfaceKey,
                    ShaderGraphDocument.Encode(LoadGraphNodeInterface(creation.sourceId), serialization, context));
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
                string selected = ShaderGraphDocument.Read(node, "function", "", serialization, context);
                module = frontends.AnalyzeModule(ShaderSourceBundle.Decode(ShaderSourceBundle.Read(function, assets), selected, serialization));
                implementation = function.implementationId;
            }
        }
        GraphNodeRecord described = node;
        if (node.definitionId == ShaderGraphNodes.callDefinitionId)
        {
            Guid id = ShaderGraphDocument.Read(node, "sourceId", Guid.Empty, serialization, context);
            if (id != Guid.Empty && TryLoadGraphNodeInterface(id, out ShaderGraphNodeInterface? nodeInterface))
            {
                described = new GraphNodeRecord(node.id, node.definitionId) { position = node.position };
                foreach ((string key, GraphSerializedValue value) in node.values) described.SetValue(key, value);
                described.SetValue(ShaderGraphNodes.interfaceKey,
                    ShaderGraphDocument.Encode(nodeInterface!, serialization, context));
            }
        }
        return nodes.DescribePorts(described, serialization, context, module, implementation, input);
    }

    internal ShaderGraphNodeInterface LoadGraphNodeInterface(Guid id)
    {
        return TryLoadGraphNodeInterface(id, out ShaderGraphNodeInterface? nodeInterface)
            ? nodeInterface!
            : throw new InvalidOperationException("Graph node Shader is unavailable or has import diagnostics.");
    }

    private bool TryLoadGraphNodeInterface(Guid id, out ShaderGraphNodeInterface? nodeInterface)
    {
        nodeInterface = null;
        if (!assets.TryGetInfo(id, out AssetInfo? info) || info is null || info.status != AssetImportStatus.Imported
            || !assets.TryLoad(id, out ShaderAsset? shader) || shader is null || shader.isMissing)
            return false;
        GraphDocument graph = ShaderGraphArtifact.ReadDocument(ShaderGraphArtifact.Read(shader, assets), serialization);
        nodeInterface = ShaderGraphNodes.ReadInterface(graph, serialization, context);
        return true;
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
        if (creation.definitionId is ShaderGraphNodes.inputDefinitionId or ShaderGraphNodes.outputDefinitionId
            && Controller(draft).document.nodes.Any(node => node.definitionId == creation.definitionId)) return false;
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
        if (node.definitionId is ShaderGraphNodes.inputDefinitionId or ShaderGraphNodes.outputDefinitionId
            && !ShaderGraphNodes.IsNodeGraph(graph))
            ShaderGraphNodes.WriteSettings(graph, new ShaderGraphNodeSettings(), serialization, context);
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
