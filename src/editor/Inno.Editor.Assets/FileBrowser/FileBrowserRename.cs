using System;

using Inno.Core.Logging;
using Inno.Editor.Assets.Selection;
using Inno.Editor.Core;
using Inno.Editor.Core.Commands;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Widgets;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Assets.FileBrowser;

internal enum FileBrowserPresentation
{
    Tree,
    List,
    Grid
}

internal sealed class FileBrowserRename(AssetEditorModule assets)
{
    private const nuint C_NAME_BUFFER_SIZE = 512;

    private EditorActionInteraction<string>? m_activeInteraction;
    private FileBrowserPresentation m_lastPresentation = FileBrowserPresentation.List;
    private FileBrowserPresentation m_activePresentation;
    private FileBrowserPresentation m_pendingPresentation;
    private string? m_pendingPath;
    private float m_pendingDeadline;
    private bool m_requestFocus;

    internal void Update(EditorContext context)
    {
        AssetSelectionTarget? target = context.selection.selectedTarget as AssetSelectionTarget;
        if (m_activeInteraction is { isCompleted: false } &&
            !Equals(m_activeInteraction.target, target))
        {
            m_activeInteraction.Cancel();
        }
        EditorActionInteraction<string>? interaction = null;
        if (target is not null)
        {
            _ = context.TryGetInteraction(
                EditorActionIds.Rename,
                typeof(AssetSurface.Browser),
                target,
                out interaction);
        }
        if (ReferenceEquals(interaction, m_activeInteraction))
            return;
        m_activeInteraction = interaction;
        m_activePresentation = m_lastPresentation;
        m_requestFocus = interaction is not null;
        CancelDelayedActivation();
    }

    internal void MarkInteraction(FileBrowserPresentation presentation)
    {
        m_lastPresentation = presentation;
        CancelDelayedActivation();
    }

    internal bool HandleActivation(
        EditorContext context,
        string relativePath,
        FileBrowserPresentation presentation,
        bool wasSelected,
        bool doubleClicked)
    {
        m_lastPresentation = presentation;
        if (doubleClicked)
        {
            CancelDelayedActivation();
            return true;
        }
        if (!wasSelected)
        {
            CancelDelayedActivation();
            return false;
        }

        m_pendingPath = relativePath;
        m_pendingPresentation = presentation;
        m_pendingDeadline = context.totalTime + NativeImGui.GetIO().MouseDoubleClickTime;
        return false;
    }

    internal void TryBeginDelayed(
        EditorContext context,
        string relativePath,
        FileBrowserPresentation presentation)
    {
        if (!string.Equals(m_pendingPath, relativePath, StringComparison.Ordinal) ||
            m_pendingPresentation != presentation ||
            context.totalTime < m_pendingDeadline)
        {
            return;
        }

        bool shouldRename = NativeImGui.IsItemHovered() &&
                            !NativeImGui.IsMouseDragging(ImGuiMouseButton.Left) &&
                            string.Equals(
                                assets.browser.GetSelectedPath(context),
                                relativePath,
                                StringComparison.Ordinal);
        CancelDelayedActivation();
        if (!shouldRename)
            return;

        _ = context.Execute(
            EditorActionIds.Rename,
            typeof(AssetSurface.Browser),
            new AssetSelectionTarget(relativePath));
        Update(context);
    }

    internal bool IsEditing(
        EditorContext context,
        string relativePath,
        FileBrowserPresentation presentation)
    {
        Update(context);
        return m_activeInteraction?.target is AssetSelectionTarget target &&
               !m_activeInteraction.isCompleted &&
               m_activePresentation == presentation &&
               string.Equals(target.relativePath, relativePath, StringComparison.Ordinal);
    }

    internal void Draw(
        EditorContext context,
        string id,
        string relativePath,
        FileBrowserPresentation presentation,
        float width)
    {
        if (!IsEditing(context, relativePath, presentation) || m_activeInteraction is null)
            return;

        string buffer = m_activeInteraction.state;
        InlineRenameResult result = ImGuiWidget.InlineRename(
            id,
            ref buffer,
            ref m_requestFocus,
            C_NAME_BUFFER_SIZE,
            MathF.Max(1f, width));
        m_activeInteraction.state = buffer;
        if (result == InlineRenameResult.Cancel)
        {
            m_activeInteraction.Cancel();
            return;
        }
        if (result != InlineRenameResult.Commit)
            return;

        try
        {
            EditorValidationResult validation = m_activeInteraction.Complete();
            if (!validation.isValid)
                Log.Warn("Asset rename was rejected: {0}", validation.message);
        }
        catch (Exception exception)
        {
            Log.Error(
                "Failed to rename asset to '{0}': {1}",
                m_activeInteraction.state,
                exception);
        }
    }

    private void CancelDelayedActivation()
    {
        m_pendingPath = null;
        m_pendingDeadline = 0f;
    }
}
