using System;
using System.Collections.Generic;

using Inno.Editor.Core;
using Inno.Editor.ImGui;
using Inno.Editor.ImGui.Renderers;
using Inno.Editor.ImGui.Widgets;

namespace Inno.Editor.Interactions.Panels;

internal sealed class EditorModalHost
{
    private readonly Dictionary<string, Transition> m_transitions = new(StringComparer.Ordinal);

    internal bool Update(
        IReadOnlyList<EditorExtensionCatalog.ModalRegistration> modals,
        double now)
    {
        bool blocksInteraction = false;
        for (int i = 0; i < modals.Count; i++)
        {
            EditorExtensionCatalog.ModalRegistration registration = modals[i];
            Transition transition = GetTransition(registration.attribute.id);
            transition.Update(registration.modal.isVisible, now);
            if (registration.modal.blocksInteraction && transition.isVisible)
                blocksInteraction = true;
        }
        return blocksInteraction;
    }

    internal void Draw(
        EditorContext context,
        IReadOnlyList<EditorExtensionCatalog.ModalRegistration> modals,
        double now)
    {
        for (int i = 0; i < modals.Count; i++)
        {
            EditorExtensionCatalog.ModalRegistration registration = modals[i];
            Transition transition = GetTransition(registration.attribute.id);
            if (!transition.isVisible)
            {
                EditorModalRenderer.Close(registration.attribute.id, registration.attribute.title);
                continue;
            }
            EditorModalRenderer.Draw(
                registration.attribute.id,
                registration.attribute.title,
                transition.GetAlpha(now),
                () => registration.modal.Draw(context));
        }
    }

    internal void Clear()
    {
        m_transitions.Clear();
    }

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
                    m_visibleAt + ImGuiWidget.style.modalMinimumVisibleSeconds);
            }
            m_requested = requested;
            if (isVisible && !requested &&
                now >= m_hideAt + ImGuiWidget.style.modalFadeOutSeconds)
            {
                isVisible = false;
            }
        }

        internal float GetAlpha(double now)
        {
            if (m_requested || now <= m_hideAt)
            {
                double fadeIn = (now - m_visibleAt) / ImGuiWidget.style.modalFadeInSeconds;
                return (float)Math.Clamp(fadeIn, 0.05, 1.0);
            }
            double fadeOut = (now - m_hideAt) / ImGuiWidget.style.modalFadeOutSeconds;
            return (float)Math.Clamp(1.0 - fadeOut, 0.05, 1.0);
        }
    }
}
