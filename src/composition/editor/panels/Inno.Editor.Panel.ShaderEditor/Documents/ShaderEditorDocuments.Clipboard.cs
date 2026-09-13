using System;
using Inno.Core.Graphs;
using Inno.Editor.Graph;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed record ShaderClipboardData(byte[] graph, Guid owner);

internal sealed partial class ShaderEditorDocuments
{
    internal ShaderClipboardData Copy(Draft draft)
        => new(GraphDocumentCodec.Encode(ShaderGraphClipboard.Copy(Controller(draft).document, draft.canvas.selectedNodes, serialization, context), serialization), draft.id);

    internal void Paste(Draft draft, ShaderClipboardData clipboard)
    {
        GraphDocumentController controller = Controller(draft);
        ShaderGraphPasteResult result = ShaderGraphClipboard.Paste(controller.document,
            GraphDocumentCodec.Decode(clipboard.graph, serialization), clipboard.owner == draft.id, draft.activeStage, serialization, context);
        controller.ReplaceDocument(result.document, "Paste Shader Nodes");
        draft.canvas.SelectNodes(result.insertedNodes);
        Changed(draft);
    }
}
