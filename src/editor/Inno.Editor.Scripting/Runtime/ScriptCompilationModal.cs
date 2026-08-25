using System.Numerics;

using Inno.Editor.Core;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Native.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Scripting;

/// <summary>Displays real script compiler progress while blocking editor interaction.</summary>
[EditorModal("scripting.compilation", "Compiling Scripts", order: 100)]
internal sealed class ScriptCompilationModal(EditorScripting scripting) : EditorModal
{
    /// <inheritdoc />
    public override bool isVisible => scripting.isCompiling;

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        float progress = scripting.progress;
        NativeImGui.TextUnformatted(scripting.status);
        EditorWidget.CenteredProgressBar(progress, new Vector2(-1f, 0f), $"{progress:P0}");
    }
}
