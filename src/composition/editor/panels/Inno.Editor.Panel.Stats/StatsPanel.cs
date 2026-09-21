using System.Collections.Generic;
using System.Linq;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.ImGuiWidget;
using Inno.Native.ImGui;
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

    public override bool useWindowPadding => false;

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

        DrawGroup("Frame Timing",
        [
            ("Time", $"{context.frame.totalTime:F2} s"),
            ("Delta", $"{m_statistics.deltaTime * 1000f:F2} ms"),
            ("FPS", $"{m_statistics.framesPerSecond:F1}")
        ]);
        IReadOnlyList<EditorStatistic> statistics = context.statistics.GetSnapshot();
        if (statistics.Count == 0)
        {
            EditorWidget.Hint("No feature statistics were published for the current frame.");
            return;
        }

        foreach (IGrouping<EditorStatisticGroupId, EditorStatistic> group in statistics.GroupBy(
                     static statistic => statistic.groupId))
        {
            DrawGroup(
                group.First().groupName,
                group.Select(static statistic => (statistic.label, statistic.value)));
        }
    }

    private static void DrawGroup(string title, IEnumerable<(string label, string value)> values)
    {
        EditorWidget.CollectionSectionHeader(title);

        System.Numerics.Vector2 padding = EditorWidget.style.inspectorSectionPadding;
        ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchProp
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.NoSavedSettings
            | ImGuiTableFlags.PadOuterX;
        NativeImGui.PushStyleVar(
            ImGuiStyleVar.CellPadding,
            new System.Numerics.Vector2(
                padding.X,
                EditorWidget.style.statisticRowPadding));
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBg, EditorPalette.collectionRow);
        NativeImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, EditorPalette.collectionRowAlternate);
        try
        {
            if (!NativeImGui.BeginTable($"##stats_{title}", 2, flags))
                return;
            try
            {
                NativeImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch, 0.42f);
                NativeImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch, 0.58f);
                foreach ((string label, string value) in values)
                {
                    NativeImGui.TableNextRow();
                    NativeImGui.TableSetColumnIndex(0);
                    NativeImGui.TextDisabled(label);
                    NativeImGui.TableSetColumnIndex(1);
                    EditorWidget.WrappedText(value);
                }
            }
            finally
            {
                NativeImGui.EndTable();
            }
        }
        finally
        {
            NativeImGui.PopStyleColor(2);
            NativeImGui.PopStyleVar();
        }
    }
}
