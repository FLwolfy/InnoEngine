using Inno.Editor.Core;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Shows lightweight runtime metrics.
/// </summary>
public sealed class StatsPanel : EditorPanel
{
    private readonly FrameStatisticsSampler m_statistics = new();

    /// <summary>
    /// Creates the panel.
    /// </summary>
    public StatsPanel()
        : base("runtime.stats", "Stats")
    {
    }

    /// <inheritdoc />
    public override void OnRender(EditorContext context)
    {
        m_statistics.Update(context.totalTime, context.frameDeltaTime);

        NativeImGui.TextUnformatted($"Time: {context.totalTime:F2}s");
        NativeImGui.TextUnformatted($"Delta: {m_statistics.deltaTime * 1000f:F2} ms");
        NativeImGui.TextUnformatted($"FPS: {m_statistics.framesPerSecond:F1}");
        NativeImGui.Separator();
        ImGuiWidget.Hint("No Scene/Game panel in this phase.");
    }
}
