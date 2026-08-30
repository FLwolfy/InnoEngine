using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.Stats;

/// <summary>
/// Shows lightweight runtime metrics.
/// </summary>
[EditorPanel("diagnostics.stats", "Stats", order: 500, menuPath: "Diagnostics")]
internal sealed class StatsPanel : EditorPanel
{
    private readonly FrameStatisticsSampler m_statistics = new();

    /// <summary>
    /// Creates the panel.
    /// </summary>
    internal StatsPanel()
    {
    }

    /// <inheritdoc />
    protected override void OnDraw(EditorContext context)
    {
        m_statistics.Update(context.frame.totalTime, context.frame.deltaTime);

        NativeImGui.TextUnformatted($"Time: {context.frame.totalTime:F2}s");
        NativeImGui.TextUnformatted($"Delta: {m_statistics.deltaTime * 1000f:F2} ms");
        NativeImGui.TextUnformatted($"FPS: {m_statistics.framesPerSecond:F1}");
        NativeImGui.Separator();
        EditorWidget.Hint("No Scene/Game panel in this phase.");
    }
}
