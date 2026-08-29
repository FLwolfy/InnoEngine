using System.Numerics;

using Inno.Editor.Core;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Scripting;

/// <summary>Displays blocking script compiler and reload progress with cancellation controls.</summary>
[EditorModal("scripting.compilation", "Compiling Scripts", order: 100)]
internal sealed class ScriptCompilationModal(EditorScripting scripting) : EditorModal
{
    /// <inheritdoc />
    public override bool isVisible => scripting.isCompiling;

    /// <inheritdoc />
    public override bool blocksInteraction => true;

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        float progress = scripting.progress;
        EditorWidget.WrappedText(scripting.status);
        EditorWidget.CenteredProgressBar(progress, new Vector2(-1f, 0f), $"{progress:P0}");
        if (EditorWidget.CenteredButton("Cancel", EditorWidget.style.itemSpacing.Y))
            scripting.CancelCompilation();
    }
}
