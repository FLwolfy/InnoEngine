using System;
using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.ImGui.ImGuiWidget;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Interactions;

namespace Inno.Editor.ImGui;

internal sealed class EditorModalHost
{
    private readonly Dictionary<string, Transition> m_transitions = new(StringComparer.Ordinal);

    internal bool Update(IReadOnlyList<EditorModalExtension> modals, double now)
    {
        bool blocksInteraction = false;
        for (int i = 0; i < modals.Count; i++)
        {
            EditorModalExtension extension = modals[i];
            Transition transition = GetTransition(extension.id);
            transition.Update(extension.modal.isVisible, now);
            if (extension.modal.blocksInteraction && transition.isVisible)
                blocksInteraction = true;
        }
        return blocksInteraction;
    }

    internal void Draw(
        EditorContext context,
        IReadOnlyList<EditorModalExtension> modals,
        double now)
    {
        for (int i = 0; i < modals.Count; i++)
        {
            EditorModalExtension extension = modals[i];
            Transition transition = GetTransition(extension.id);
            if (!transition.isVisible)
            {
                EditorModalRenderer.Close(extension.id, extension.title);
                continue;
            }
            EditorModalRenderer.Draw(
                extension.id,
                extension.title,
                transition.GetAlpha(now),
                extension.modal,
                context);
        }
    }

    internal void Clear() => m_transitions.Clear();

    private Transition GetTransition(string id)
    {
        if (m_transitions.TryGetValue(id, out Transition? transition))
            return transition;
        transition = new Transition();
        m_transitions.Add(id, transition);
        return transition;
    }

    private sealed class Transition
    {
        private double m_visibleAt;
        private double m_hideAt;
        private bool m_requested;

        internal bool isVisible { get; private set; }

        internal void Update(bool requested, double now)
        {
            if (requested)
            {
                if (!m_requested)
                {
                    if (!isVisible)
                        m_visibleAt = now;
                    isVisible = true;
                }
                m_hideAt = double.PositiveInfinity;
            }
            else if (m_requested)
            {
                m_hideAt = Math.Max(
                    now,
                    m_visibleAt + EditorWidget.style.modalMinimumVisibleSeconds);
            }
            m_requested = requested;
            if (isVisible && !requested &&
                now >= m_hideAt + EditorWidget.style.modalFadeOutSeconds)
            {
                isVisible = false;
            }
        }

        internal float GetAlpha(double now)
        {
            if (m_requested || now <= m_hideAt)
            {
                double fadeIn = (now - m_visibleAt) / EditorWidget.style.modalFadeInSeconds;
                return (float)Math.Clamp(fadeIn, 0.05, 1.0);
            }
            double fadeOut = (now - m_hideAt) / EditorWidget.style.modalFadeOutSeconds;
            return (float)Math.Clamp(1.0 - fadeOut, 0.05, 1.0);
        }
    }
}
