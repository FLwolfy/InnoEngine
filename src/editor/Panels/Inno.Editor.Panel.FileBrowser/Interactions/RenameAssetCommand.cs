using System;

using Inno.Assets.Pipeline;
using Inno.Core.Input;
using Inno.Core.Logging;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Editor.Interactions;

using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using static Inno.Editor.Panel.FileBrowser.FileBrowserUtility;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_RENAME, priority: 100)]
[EditorMenu(FileBrowserInteractionIds.C_AREA, "Rename", order: 100)]
[EditorShortcut(FileBrowserInteractionIds.C_AREA, KeyCode.F2)]
internal sealed class RenameAssetCommand(AssetEditorModule assets, LogRouter logs) :
    EditorPresentationAction<AssetFileEntry, InlineRenamePresentation>
{
    private readonly Logger m_log = (logs ?? throw new ArgumentNullException(nameof(logs)))
        .CreateLogger<RenameAssetCommand>();
    private AssetEditorContext? m_asset;
    private string m_buffer = string.Empty;
    private bool m_requestFocus;

    /// <summary>
    /// Evaluates whether the requested change can be applied to the current generation.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated editor action state that represents the completed operation.
    /// </returns>
    protected override EditorActionState Query(EditorActionContext<AssetFileEntry> context)
        => !context.target.isReadOnly && TryGetAssetContext(context, out _)
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    /// <summary>
    /// Executes the prepared operation and publishes only a completed result.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        if (!TryGetAssetContext(context, out AssetEditorContext? assetContext) || assetContext is null)
            return;
        Activate(context);
        m_asset = assetContext;
        m_buffer = GetEditableName(assetContext.name, assetContext.isDirectory);
        m_requestFocus = true;
    }

    /// <summary>
    /// Presents this action through the current editor interaction surface.
    /// </summary>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
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

    /// <summary>
    /// Commits the interaction after editing completes successfully.
    /// </summary>
    protected override void OnCompleted() => ClearState();

    /// <summary>
    /// Cancels the interaction without committing its pending value.
    /// </summary>
    protected override void OnCancelled() => ClearState();

    /// <summary>
    /// Cancels pending presentation state when its editor surface disappears.
    /// </summary>
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
            m_log.Write(LogLevel.Warn, "Asset rename was rejected: {0}", [validation.message]);
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
        => assets.TryCreateContext(context.editor, context.target.assetPath.ToString(), out assetContext);
}
