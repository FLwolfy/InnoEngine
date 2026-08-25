using System;

using Inno.Assets.File;
using Inno.Assets;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.FileBrowser;

internal enum FileBrowserPresentation
{
    Tree,
    List,
    Grid
}

internal sealed class FileBrowserRename(AssetEditorModule assets)
{
    private AssetFileEntry? m_activeTarget;
    private FileBrowserPresentation m_lastPresentation = FileBrowserPresentation.List;
    private FileBrowserPresentation m_activePresentation;
    private FileBrowserPresentation m_pendingPresentation;
    private string? m_pendingPath;
    private float m_pendingDeadline;

    internal void Update(EditorContext context)
    {
        AssetFileEntry? target = assets.interactions.selection.selectedTarget as AssetFileEntry;
        bool isActive = target is not null &&
                        assets.interactions.For("panel/asset.file-browser", target)
                            .IsActive("file-browser/rename");
        if (isActive && Equals(target, m_activeTarget))
            return;
        m_activeTarget = isActive ? target : null;
        m_activePresentation = m_lastPresentation;
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
        m_pendingDeadline = context.frame.totalTime + NativeImGui.GetIO().MouseDoubleClickTime;
        return false;
    }

    internal void TryBeginDelayed(
        EditorContext context,
        string relativePath,
        FileBrowserPresentation presentation)
    {
        if (!string.Equals(m_pendingPath, relativePath, StringComparison.Ordinal) ||
            m_pendingPresentation != presentation ||
            context.frame.totalTime < m_pendingDeadline)
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

        _ = assets.interactions
            .For(
                "panel/asset.file-browser",
                AssetManager.TryGetFileSystemEntry(relativePath, out AssetFileEntry entry) ? entry : null)
            .Execute("file-browser/rename");
        Update(context);
    }

    internal bool IsEditing(
        EditorContext context,
        string relativePath,
        FileBrowserPresentation presentation)
    {
        Update(context);
        return m_activeTarget is AssetFileEntry target &&
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
        if (!IsEditing(context, relativePath, presentation) || m_activeTarget is null)
            return;
        _ = assets.interactions
            .For("panel/asset.file-browser", m_activeTarget)
            .Present(
                "file-browser/rename",
                new InlineRenamePresentation(id, MathF.Max(1f, width)));
    }

    private void CancelDelayedActivation()
    {
        m_pendingPath = null;
        m_pendingDeadline = 0f;
    }
}
