using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Editor.Graph;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed record ShaderClipboardData(byte[] graph, Guid owner);

internal sealed partial class ShaderEditorDocuments
{
    internal ShaderClipboardData Copy(Draft draft)
        => new(GraphDocumentCodec.Encode(ShaderGraphClipboard.Copy(Controller(draft).document, draft.canvas.selectedNodes, serialization, context), serialization), draft.id);

    internal bool CanPaste(Draft draft, ShaderClipboardData data)
    {
        GraphDocument destination = Controller(draft).document;
        GraphDocument fragment = GraphDocumentCodec.Decode(data.graph, serialization);
        var existingStages = destination.nodes.Where(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .Select(node => ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), serialization, context).stage)
            .ToHashSet();
        return !fragment.nodes.Where(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .Select(node => ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), serialization, context).stage)
            .Any(existingStages.Contains);
    }

    internal void Paste(Draft draft, ShaderClipboardData clipboard)
    {
        if (!CanPaste(draft, clipboard))
            throw new InvalidOperationException("The shader already contains an output for one of the copied stages.");
        GraphDocumentController controller = Controller(draft);
        ShaderGraphPasteResult result = ShaderGraphClipboard.Paste(controller.document,
            GraphDocumentCodec.Decode(clipboard.graph, serialization), clipboard.owner == draft.id, draft.activeStage, serialization, context);
        controller.ReplaceDocument(result.document, "Paste Shader Nodes");
        draft.canvas.SelectNodes(result.insertedNodes);
        Changed(draft);
    }
}
