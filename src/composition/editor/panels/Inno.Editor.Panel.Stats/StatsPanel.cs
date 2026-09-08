using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Draws this feature using the current editor presentation context.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    protected override void OnDraw(EditorContext context)
    {
        m_statistics.Update(context.frame.totalTime, context.frame.deltaTime);

        NativeImGui.TextUnformatted($"Time: {context.frame.totalTime:F2}s");
        NativeImGui.TextUnformatted($"Delta: {m_statistics.deltaTime * 1000f:F2} ms");
        NativeImGui.TextUnformatted($"FPS: {m_statistics.framesPerSecond:F1}");
        IReadOnlyList<EditorStatistic> statistics = context.statistics.GetSnapshot();
        if (statistics.Count == 0)
        {
            NativeImGui.Separator();
            EditorWidget.Hint("No feature statistics were published for the current frame.");
            return;
        }

        foreach (IGrouping<EditorStatisticGroupId, EditorStatistic> group in statistics.GroupBy(
                     static statistic => statistic.groupId))
        {
            NativeImGui.SeparatorText(group.First().groupName);
            foreach (EditorStatistic statistic in group)
                NativeImGui.TextUnformatted($"{statistic.label}: {statistic.value}");
        }
    }
}
