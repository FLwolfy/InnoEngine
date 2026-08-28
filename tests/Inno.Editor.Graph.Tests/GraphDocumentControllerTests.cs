using System;
using System.Collections.Generic;
using Inno.Core.Graphs;
using Inno.Editor.Graph;
using Inno.Editor.Interactions;
using Xunit;

namespace Inno.Editor.Graph.Tests;

public sealed class GraphDocumentControllerTests
{
    [Fact]
    public void AddNode_RecordsNeutralSnapshotAndSupportsTransitionUndoRedo()
    {
        var module = new GraphEditorModule();
        var history = new RecordingHistory();
        var document = new GraphDocument();
        GraphDocumentController controller = module.OpenDocument("Assets/Test.ishadergraph", document, history);

        GraphNodeId nodeId = controller.AddNode("test.node", new GraphPosition(2f, 3f));
        EditorHistoryChange change = Assert.Single(history.changes);

        GraphHistoryTransitionResult undo = GraphHistoryTransition.Apply(
            module,
            change,
            EditorHistoryDirection.Undo);
        Assert.True(undo.result.succeeded);
        Assert.Empty(document.nodes);

        GraphHistoryTransitionResult redo = GraphHistoryTransition.Apply(
            module,
            change,
            EditorHistoryDirection.Redo);
        Assert.True(redo.result.succeeded);
        Assert.Equal(nodeId, Assert.Single(document.nodes).id);
        change.Dispose();
    }

    [Fact]
    public void CopyPaste_RemapsNodesAndPreservesInternalConnections()
    {
        var module = new GraphEditorModule();
        var history = new RecordingHistory();
        var document = new GraphDocument();
        GraphDocumentController controller = module.OpenDocument("Assets/Copy.graph", document, history);
        GraphNodeId first = controller.AddNode("test.first", new GraphPosition(1f, 2f));
        GraphNodeId second = controller.AddNode("test.second", new GraphPosition(3f, 4f));
        controller.Connect(
            new GraphEndpoint(first, new GraphPortId("out")),
            new GraphEndpoint(second, new GraphPortId("in")));

        GraphClipboardData clipboard = controller.Copy([first, second]);
        IReadOnlyList<GraphNodeId> pasted = controller.Paste(clipboard, new GraphPosition(10f, 20f));

        Assert.Equal(2, pasted.Count);
        Assert.DoesNotContain(first, pasted);
        Assert.DoesNotContain(second, pasted);
        Assert.Equal(4, document.nodes.Count);
        Assert.Equal(2, document.edges.Count);
        GraphNodeRecord pastedFirst = document.FindNode(pasted[0])!;
        Assert.Equal(new GraphPosition(11f, 22f), pastedFirst.position);
        history.DisposeChanges();
    }

    [Fact]
    public void CanvasZoom_PreservesGraphPointUnderPivot()
    {
        var canvas = new GraphCanvasState();
        canvas.SetViewport(new GraphPosition(10f, 20f), 1f);

        canvas.ZoomAt(2f, 110f, 220f);

        Assert.Equal(2f, canvas.zoom);
        Assert.Equal(new GraphPosition(-90f, -180f), canvas.pan);
    }

    private sealed class RecordingHistory : IEditorHistory
    {
        public List<EditorHistoryChange> changes { get; } = [];
        public bool canUndo => false;
        public bool canRedo => false;
        public bool isFaulted => false;
        public string? undoName => null;
        public string? redoName => null;
        public string? undoUnavailableReason => null;
        public string? redoUnavailableReason => null;
        public string? faultReason => null;
        public long residentBytes => 0;
        public long diskBytes => 0;
        public EditorHistoryTransaction BeginTransaction(string name) => throw new NotSupportedException();
        public EditorHistoryResult Execute(string name, EditorHistoryChange change) => throw new NotSupportedException();
        public void RecordApplied(string name, EditorHistoryChange change)
        {
            _ = name;
            changes.Add(change);
        }
        public EditorHistoryResult Undo() => throw new NotSupportedException();
        public EditorHistoryResult Redo() => throw new NotSupportedException();
        public void DisposeChanges()
        {
            foreach (EditorHistoryChange change in changes)
            {
                change.Dispose();
            }

            changes.Clear();
        }
    }
}
