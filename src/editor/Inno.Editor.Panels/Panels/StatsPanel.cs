using System;
using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using NativeImGui = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panels;

/// <summary>
/// Shows lightweight runtime metrics.
/// </summary>
public sealed class StatsPanel : EditorPanel
{
    private const float C_SAMPLE_WINDOW_SECONDS = 0.5f;
    private const float C_SAMPLE_RESET_SECONDS = 1f;

    private readonly Queue<float> m_frameDurations = [];
    private double m_sampleDuration;
    private float m_lastSampleTime = float.NaN;
    private float m_smoothedDeltaTime;
    private float m_smoothedFps;

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
        UpdateFrameStatistics(context.totalTime, context.frameDeltaTime);

        NativeImGui.TextUnformatted($"Time: {context.totalTime:F2}s");
        NativeImGui.TextUnformatted($"Delta: {m_smoothedDeltaTime * 1000f:F2} ms");
        NativeImGui.TextUnformatted($"FPS: {m_smoothedFps:F1}");
        NativeImGui.Separator();
        ImGuiWidget.Hint("No Scene/Game panel in this phase.");
    }

    private void UpdateFrameStatistics(float totalTime, float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
            return;

        if (float.IsFinite(m_lastSampleTime) && totalTime - m_lastSampleTime > C_SAMPLE_RESET_SECONDS)
            ResetFrameStatistics();
        m_lastSampleTime = totalTime;

        m_frameDurations.Enqueue(deltaTime);
        m_sampleDuration += deltaTime;
        while (m_frameDurations.Count > 1 &&
               m_sampleDuration - m_frameDurations.Peek() >= C_SAMPLE_WINDOW_SECONDS)
        {
            m_sampleDuration -= m_frameDurations.Dequeue();
        }

        double averageFrameDuration = m_sampleDuration / m_frameDurations.Count;
        m_smoothedDeltaTime = (float)averageFrameDuration;
        m_smoothedFps = averageFrameDuration > 0d
            ? (float)(1d / averageFrameDuration)
            : 0f;
    }

    private void ResetFrameStatistics()
    {
        m_frameDurations.Clear();
        m_sampleDuration = 0d;
        m_smoothedDeltaTime = 0f;
        m_smoothedFps = 0f;
    }
}
