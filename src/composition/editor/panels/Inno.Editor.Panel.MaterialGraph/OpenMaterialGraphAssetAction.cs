using Inno.Editor.Interactions;
using Inno.Rendering;

namespace Inno.Editor.Panel.MaterialGraph;

/// <summary>
/// Opens a selected material in its dedicated one-to-one node workspace.
/// </summary>
[EditorAction("editor/open", priority: 300)]
internal sealed class OpenMaterialGraphAssetAction : EditorAction<MaterialAsset, string>
{
    /// <summary>
    /// Selects the resolved asset and opens the single Material Graph workspace.
    /// </summary>
    /// <param name="context">
    /// Typed asset-open request issued by the Asset Browser.
    /// </param>
    protected override void Execute(EditorActionContext<MaterialAsset, string> context)
    {
        context.interactions.SetSelection(context.target);
        if (!context.interactions.OpenPanel(MaterialGraphPanel.C_PANEL_ID))
        {
            throw new System.InvalidOperationException(
                "The Material Graph panel is unavailable in the active Editor generation.");
        }
    }
}
