using Inno.Editor.Core;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Shows lightweight runtime metrics.
/// </summary>
public sealed class StatsPanel : EditorPanel
{
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
        float deltaTimeMs = context.frameDeltaTime * 1000f;
        float fps = context.frameDeltaTime > 0f ? 1f / context.frameDeltaTime : 0f;

        NativeImGui.TextUnformatted($"Time: {context.totalTime:F2}s");
        NativeImGui.TextUnformatted($"Delta: {deltaTimeMs:F2} ms");
        NativeImGui.TextUnformatted($"FPS: {fps:F1}");
        NativeImGui.Separator();
        ImGuiWidget.Hint("No Scene/Game panel in this phase.");
    }
}
