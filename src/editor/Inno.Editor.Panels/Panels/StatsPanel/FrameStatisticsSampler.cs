using System;
using System.Collections.Generic;

namespace Inno.Editor.Panels;

internal sealed class FrameStatisticsSampler
{
    private const float C_SAMPLE_WINDOW_SECONDS = 0.5f;
    private const float C_SAMPLE_RESET_SECONDS = 1f;

    private readonly Queue<float> m_frameDurations = [];
    private double m_sampleDuration;
    private float m_lastSampleTime = float.NaN;

    internal float deltaTime { get; private set; }
    internal float framesPerSecond { get; private set; }

    internal void Update(float totalTime, float frameDeltaTime)
    {
        if (!float.IsFinite(frameDeltaTime) || frameDeltaTime <= 0f)
            return;

        if (float.IsFinite(m_lastSampleTime) && totalTime - m_lastSampleTime > C_SAMPLE_RESET_SECONDS)
            Reset();
        m_lastSampleTime = totalTime;

        m_frameDurations.Enqueue(frameDeltaTime);
        m_sampleDuration += frameDeltaTime;
        while (m_frameDurations.Count > 1 &&
               m_sampleDuration - m_frameDurations.Peek() >= C_SAMPLE_WINDOW_SECONDS)
        {
            m_sampleDuration -= m_frameDurations.Dequeue();
        }

        double averageFrameDuration = m_sampleDuration / m_frameDurations.Count;
        deltaTime = (float)averageFrameDuration;
        framesPerSecond = averageFrameDuration > 0d ? (float)(1d / averageFrameDuration) : 0f;
    }

    private void Reset()
    {
        m_frameDurations.Clear();
        m_sampleDuration = 0d;
        deltaTime = 0f;
        framesPerSecond = 0f;
    }
}
