using System.Numerics;

using Inno.Editor.Core;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Scripting;

/// <summary>
/// Displays blocking script compiler and reload progress with cancellation controls.
/// </summary>
/// <param name="scripting">
/// The scripting used to initialize this instance.
/// </param>
[EditorModal("scripting.compilation", "Compiling Scripts", order: 100)]
internal sealed class ScriptCompilationModal(EditorScripting scripting) : EditorModal
{
    /// <summary>
    /// Gets whether this implementation is visible.
    /// </summary>
    public override bool isVisible => scripting.isCompiling;

    /// <summary>
    /// Gets whether blocks interaction is enabled for this implementation.
    /// </summary>
    public override bool blocksInteraction => true;

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnDraw(EditorContext context)
    {
        float progress = scripting.progress;
        EditorWidget.WrappedText(scripting.status);
        EditorWidget.CenteredProgressBar(progress, new Vector2(-1f, 0f), $"{progress:P0}");
        if (EditorWidget.CenteredButton("Cancel", EditorWidget.style.itemSpacing.Y))
            scripting.CancelCompilation();
    }
}
