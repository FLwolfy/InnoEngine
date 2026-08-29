using System;

using Inno.Assets.File;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Editor.ImGui.ImGuiWidget;

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

    internal void Update(EditorContext context)
    {
        AssetFileEntry? target = assets.interactions.selection.selectedTarget as AssetFileEntry;
        bool isActive = target is not null &&
                        assets.interactions.For(FileBrowserInteractionIds.C_AREA, target)
                            .IsActive(FileBrowserInteractionIds.C_RENAME);
        if (isActive && Equals(target, m_activeTarget))
            return;
        m_activeTarget = isActive ? target : null;
        m_activePresentation = m_lastPresentation;
    }

    internal void MarkInteraction(FileBrowserPresentation presentation)
    {
        m_lastPresentation = presentation;
    }

    internal bool IsEditing(
        EditorContext context,
        string relativePath,
        FileBrowserPresentation presentation)
    {
        Update(context);
        return m_activeTarget is AssetFileEntry target &&
               m_activePresentation == presentation &&
               string.Equals(target.assetPath.ToString(), relativePath, StringComparison.Ordinal);
    }

    internal void Draw(
        EditorContext context,
        string id,
        string relativePath,
        FileBrowserPresentation presentation,
        float width,
        float rowHeight)
    {
        if (!IsEditing(context, relativePath, presentation) || m_activeTarget is null)
            return;
        _ = assets.interactions
            .For(FileBrowserInteractionIds.C_AREA, m_activeTarget)
            .Present(
                FileBrowserInteractionIds.C_RENAME,
                new InlineRenamePresentation(
                    id,
                    MathF.Max(1f, width),
                    MathF.Max(1f, rowHeight)));
    }

}
