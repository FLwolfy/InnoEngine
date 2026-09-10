using System;
using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.Global;

/// <summary>
/// Hosts every asset document in one consistent, reload-safe tab surface.
/// </summary>
[EditorPanel("editor.documents", "Documents", order: 240, defaultOpen: false, menuPath: "Authoring")]
internal sealed class DocumentHostPanel : EditorPanel
{
    private const string C_CLOSE_POPUP = "Unsaved document##document-close";
    private readonly IEditorDocumentService m_documents;
    private Guid? m_pendingClose;

    /// <summary>
    /// Creates the unified document host.
    /// </summary>
    /// <param name="documents">
    /// The reload-safe document service.
    /// </param>
    internal DocumentHostPanel(IEditorDocumentService documents)
        => m_documents = documents ?? throw new ArgumentNullException(nameof(documents));

    /// <summary>
    /// Gets whether the host uses the standard panel padding.
    /// </summary>
    public override bool useWindowPadding => false;

    /// <summary>
    /// Gets whether the host window owns scrolling.
    /// </summary>
    public override bool allowScrolling => false;

    /// <summary>
    /// Draws the unified tab strip, document commands, and active provider.
    /// </summary>
    /// <param name="context">
    /// The active editor context.
    /// </param>
    protected override void OnDraw(EditorContext context)
    {
        _ = context;
        IReadOnlyList<EditorDocumentContext> documents = m_documents.documents;
        if (documents.Count == 0)
        {
            EditorWidget.Hint("Open a supported asset to begin editing.");
            return;
        }

        DrawTabs(documents);
        DrawToolbar();
        NativeImGui.Separator();

        EditorDocumentContext? active = m_documents.activeDocument;
        if (active is null)
            EditorWidget.Hint("Select a document tab.");
        else if (!active.isProviderAvailable)
            EditorWidget.Hint("The document provider is reloading. Your tab and view state are preserved.");
        else if (!m_documents.DrawActive())
            EditorWidget.Hint("The document provider is unavailable.");

        DrawCloseConfirmation();
    }

    private void DrawTabs(IReadOnlyList<EditorDocumentContext> documents)
    {
        if (!NativeImGui.BeginTabBar(
                "##document-tabs",
                ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs))
        {
            return;
        }
        try
        {
            Guid? activeId = m_documents.activeDocument?.documentId;
            for (int i = 0; i < documents.Count; i++)
            {
                EditorDocumentContext document = documents[i];
                string dirtyMarker = document.isDirty ? " *" : string.Empty;
                string label = $"{document.title}{dirtyMarker}##{document.documentId:N}";
                bool remainsOpen = true;
                ImGuiTabItemFlags flags = activeId == document.documentId
                    ? ImGuiTabItemFlags.SetSelected
                    : ImGuiTabItemFlags.None;
                bool visible = NativeImGui.BeginTabItem(label, ref remainsOpen, flags);
                if (visible)
                {
                    if (activeId != document.documentId)
                        _ = m_documents.Focus(document.documentId);
                    NativeImGui.EndTabItem();
                }
                if (!remainsOpen)
                    RequestClose(document);
            }
        }
        finally
        {
            NativeImGui.EndTabBar();
        }
    }

    private void DrawToolbar()
    {
        EditorDocumentContext? active = m_documents.activeDocument;
        bool disabled = active is null || !active.isProviderAvailable;
        NativeImGui.BeginDisabled(disabled);
        try
        {
            if (NativeImGui.Button("Save") && active is not null)
                _ = m_documents.Save(active.documentId);
            NativeImGui.SameLine();
            if (NativeImGui.Button("Apply") && active is not null)
                _ = m_documents.Apply(active.documentId);
            NativeImGui.SameLine();
            if (NativeImGui.Button("Revert") && active is not null)
                _ = m_documents.Revert(active.documentId);
            NativeImGui.SameLine();
            if (NativeImGui.Button("Save All"))
                _ = m_documents.SaveAll();
        }
        finally
        {
            NativeImGui.EndDisabled();
        }
    }

    private void RequestClose(EditorDocumentContext document)
    {
        if (!document.isDirty)
        {
            _ = m_documents.Close(document.documentId, EditorDocumentCloseMode.Discard);
            return;
        }
        m_pendingClose = document.documentId;
        NativeImGui.OpenPopup(C_CLOSE_POPUP);
    }

    private void DrawCloseConfirmation()
    {
        if (!NativeImGui.BeginPopup(C_CLOSE_POPUP))
            return;
        try
        {
            EditorDocumentContext? document = m_pendingClose is Guid id
                ? FindDocument(id)
                : null;
            NativeImGui.TextUnformatted(document is null
                ? "The document is no longer open."
                : $"Save changes to {document.title}?");
            NativeImGui.Separator();
            if (document is not null && NativeImGui.Button("Save"))
            {
                if (m_documents.Close(document.documentId, EditorDocumentCloseMode.Save))
                    FinishClosePopup();
            }
            if (document is not null)
            {
                NativeImGui.SameLine();
                if (NativeImGui.Button("Discard"))
                {
                    _ = m_documents.Close(document.documentId, EditorDocumentCloseMode.Discard);
                    FinishClosePopup();
                }
                NativeImGui.SameLine();
            }
            if (NativeImGui.Button(document is null ? "Close" : "Cancel"))
                FinishClosePopup();
        }
        finally
        {
            NativeImGui.EndPopup();
        }
    }

    private EditorDocumentContext? FindDocument(Guid documentId)
    {
        IReadOnlyList<EditorDocumentContext> documents = m_documents.documents;
        for (int i = 0; i < documents.Count; i++)
        {
            if (documents[i].documentId == documentId)
                return documents[i];
        }
        return null;
    }

    private void FinishClosePopup()
    {
        m_pendingClose = null;
        NativeImGui.CloseCurrentPopup();
    }
}
