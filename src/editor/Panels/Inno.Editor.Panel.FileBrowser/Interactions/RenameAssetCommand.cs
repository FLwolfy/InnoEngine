using Inno.Assets.File;
using Inno.Editor.Interactions;
using Inno.Core.Input;
using Inno.Core.Logging;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_RENAME, priority: 100)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Rename", order: 100)]
[EditorShortcut(FileBrowserInteractionIds.C_AREA, KeyCode.F2)]
internal sealed class RenameAssetCommand(AssetEditorModule assets) :
    EditorPresentationAction<AssetFileEntry, InlineRenamePresentation>
{
    internal static EditorCommand command { get; } = new(FileBrowserInteractionIds.rename);
    internal static EditorCommand<InlineRenamePresentation> presentationCommand { get; } =
        new(FileBrowserInteractionIds.rename);

    private AssetEditorContext? m_asset;
    private string m_buffer = string.Empty;
    private bool m_requestFocus;

    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => TryGetAssetContext(context, out _) ? EditorActionState.enabled : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return;
        Activate(context);
        m_asset = assetContext;
        m_buffer = assetContext.name;
        m_requestFocus = true;
    }

    protected override bool Present(EditorActionContext<AssetFileEntry, InlineRenamePresentation> context)
    {
        if (m_asset is null)
            return false;
        InlineRenamePresentation presentation = context.argument;

        InlineRenameResult result = EditorWidget.InlineRename(
            presentation.id,
            ref m_buffer,
            ref m_requestFocus,
            presentation.rowHeight,
            presentation.bufferSize,
            presentation.width);
        if (result == InlineRenameResult.Cancel)
        {
            Cancel();
            return true;
        }
        if (result == InlineRenameResult.FocusLost)
        {
            _ = TryCommit(keepActiveWhenInvalid: false);
            return true;
        }
        if (result != InlineRenameResult.Commit)
            return true;
        _ = TryCommit(keepActiveWhenInvalid: true);
        return true;
    }

    protected override void OnCompleted() => ClearState();

    protected override void OnCancelled() => ClearState();

    protected override void OnPresentationLost()
        => _ = TryCommit(keepActiveWhenInvalid: false);

    private bool TryCommit(bool keepActiveWhenInvalid)
    {
        if (m_asset is null)
        {
            Cancel();
            return false;
        }

        EditorValidationResult validation = assets.ValidateRename(m_asset, m_buffer);
        if (!validation.isValid)
        {
            Log.Warn("Asset rename was rejected: {0}", validation.message);
            if (keepActiveWhenInvalid)
                m_requestFocus = true;
            else
                Cancel();
            return false;
        }

        AssetEditorContext asset = m_asset;
        string name = m_buffer;
        Complete();
        assets.Rename(asset, name);
        return true;
    }

    private void ClearState()
    {
        m_asset = null;
        m_buffer = string.Empty;
        m_requestFocus = false;
    }

    private bool TryGetAssetContext(
        EditorActionContext<AssetFileEntry> context,
        out AssetEditorContext? assetContext)
        => assets.TryCreateContext(context.editor, context.target.relativePath, out assetContext);
}
