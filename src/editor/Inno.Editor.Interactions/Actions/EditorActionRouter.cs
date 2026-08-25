using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

internal sealed class EditorActionRouter(
    EditorExtensionCatalog catalog,
    EditorContext editor,
    EditorInteractions interactions)
{
    private readonly Queue<(string Action, EditorActionContext Context)> m_pending = [];
    private readonly HashSet<string> m_presentationFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_queryFailures = new(StringComparer.Ordinal);

    internal EditorActionState Query(string action, EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(action, context);
        if (registration is null)
            return EditorActionState.hidden;
        try
        {
            EditorActionState state = registration.action.QueryInternal(context);
            m_queryFailures.Remove(action);
            return state;
        }
        catch (Exception exception)
        {
            if (m_queryFailures.Add(action))
                Log.Error("Editor action '{0}' query failed: {1}", action, exception);
            return EditorActionState.disabled;
        }
    }

    internal bool Execute(string action, EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(action, context);
        if (registration is null)
            return false;
        try
        {
            EditorActionState state = registration.action.QueryInternal(context);
            if (!state.isVisible || !state.isEnabled)
                return false;
            registration.action.ExecuteInternal(context);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("Editor action '{0}' failed: {1}", action, exception);
            return false;
        }
    }

    internal bool Present(string action, EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(action, context);
        if (registration is null)
            return false;
        try
        {
            bool presented = registration.action.PresentInternal(context);
            m_presentationFailures.Remove(action);
            return presented;
        }
        catch (Exception exception)
        {
            registration.action.CancelInternal();
            if (m_presentationFailures.Add(action))
                Log.Error("Editor action '{0}' presentation failed: {1}", action, exception);
            return false;
        }
    }

    internal bool IsActive(string action, EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(action, context);
        return registration is not null && registration.action.IsActiveFor(context.target);
    }

    internal void Enqueue(string action, EditorActionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(context);
        m_pending.Enqueue((action, context));
    }

    internal void Flush()
    {
        while (m_pending.TryDequeue(out (string Action, EditorActionContext Context) request))
            _ = Execute(request.Action, request.Context);
    }

    internal void Clear()
    {
        m_pending.Clear();
        m_presentationFailures.Clear();
        m_queryFailures.Clear();
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
            registration.action.CancelInternal();
    }

    internal void ResetTransientState()
    {
        m_pending.Clear();
        m_presentationFailures.Clear();
        m_queryFailures.Clear();
    }

    internal void LosePresentationExcept(object? target)
    {
        var visited = new HashSet<EditorAction>(ReferenceEqualityComparer.Instance);
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            EditorAction action = registration.action;
            if (!visited.Add(action) || !action.isActive || action.IsActiveFor(target))
                continue;
            try
            {
                action.LosePresentationInternal();
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Editor action '{0}' failed while losing presentation focus: {1}",
                    registration.attribute.action,
                    exception);
            }
        }
    }

    internal bool TryGetShortcut(
        string action,
        string area,
        object? target,
        out HotKeyGesture gesture)
    {
        var context = new EditorActionContext(editor, interactions, new EditorAreaId(area), target);
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(action, context);
        return TryResolveShortcut(registration, area, out gesture);
    }

    internal bool DispatchShortcut(KeyPressedEvent keyEvent, string area, object? target)
    {
        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
            _ = actionIds.Add(registration.id.value);
        var context = new EditorActionContext(editor, interactions, new EditorAreaId(area), target);
        var candidates = new List<ShortcutCandidate>();
        foreach (string action in actionIds)
        {
            ResolvedAction? resolved = ResolveWithSpecificity(action, context);
            if (resolved is null ||
                !TryResolveShortcut(resolved.Value.registration, area, out HotKeyGesture gesture) ||
                !gesture.Matches(keyEvent))
            {
                continue;
            }
            candidates.Add(new ShortcutCandidate(action, resolved.Value, gesture));
        }
        candidates.Sort(static (left, right) =>
        {
            int areaComparison = right.resolved.exactArea.CompareTo(left.resolved.exactArea);
            if (areaComparison != 0)
                return areaComparison;
            int distanceComparison = left.resolved.targetDistance.CompareTo(right.resolved.targetDistance);
            if (distanceComparison != 0)
                return distanceComparison;
            int priorityComparison = right.resolved.registration.attribute.priority.CompareTo(
                left.resolved.registration.attribute.priority);
            if (priorityComparison != 0)
                return priorityComparison;
            return string.Compare(left.action, right.action, StringComparison.Ordinal);
        });
        for (int i = 0; i < candidates.Count; i++)
        {
            ShortcutCandidate candidate = candidates[i];
            if (Execute(candidate.action, context))
                return true;
        }
        return false;
    }

    private static bool TryResolveShortcut(
        EditorExtensionCatalog.ActionRegistration? registration,
        string area,
        out HotKeyGesture gesture)
    {
        EditorShortcutAttribute? best = null;
        if (registration is not null)
        {
            foreach (EditorShortcutAttribute shortcut in registration.shortcuts)
            {
                if (!string.IsNullOrEmpty(shortcut.area) &&
                    !string.Equals(shortcut.area, area, StringComparison.Ordinal))
                {
                    continue;
                }
                if (best is null || string.IsNullOrEmpty(best.area) && !string.IsNullOrEmpty(shortcut.area))
                    best = shortcut;
            }
        }
        if (best is null)
        {
            gesture = default;
            return false;
        }
        gesture = CreateGesture(best);
        return true;
    }

    private EditorExtensionCatalog.ActionRegistration? Resolve(
        string action,
        EditorActionContext context)
        => ResolveWithSpecificity(action, context)?.registration;

    private ResolvedAction? ResolveWithSpecificity(
        string action,
        EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? best = null;
        int bestDistance = int.MaxValue;
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            if (!string.Equals(registration.id.value, action, StringComparison.Ordinal))
                continue;
            bool exactArea = registration.area is not null;
            if (exactArea &&
                registration.area != context.area)
            {
                continue;
            }

            int distance = int.MaxValue;
            Type? targetType = registration.action.targetType;
            if (targetType is not null)
            {
                if (context.target is null ||
                    !EditorTypeDistance.TryGet(context.target.GetType(), targetType, out distance))
                {
                    continue;
                }
            }
            Type? argumentType = registration.action.argumentType;
            if (argumentType is not null &&
                (context.argument is null || !argumentType.IsInstanceOfType(context.argument)))
            {
                continue;
            }

            bool bestExactArea = best?.area is not null;
            if (best is null ||
                exactArea && !bestExactArea ||
                exactArea == bestExactArea &&
                (distance < bestDistance ||
                 distance == bestDistance && registration.attribute.priority > best.attribute.priority ||
                 distance == bestDistance && registration.attribute.priority == best.attribute.priority &&
                 string.Compare(registration.type.FullName, best.type.FullName, StringComparison.Ordinal) < 0))
            {
                best = registration;
                bestDistance = distance;
            }
        }
        return best is null
            ? null
            : new ResolvedAction(best, bestDistance, best.area is not null);
    }

    private static HotKeyGesture CreateGesture(EditorShortcutAttribute shortcut)
        => shortcut.primary
            ? HotKeyGesture.Primary(shortcut.key, shortcut.modifiers)
            : new HotKeyGesture(shortcut.key, shortcut.modifiers);

    private readonly record struct ResolvedAction(
        EditorExtensionCatalog.ActionRegistration registration,
        int targetDistance,
        bool exactArea);

    private readonly record struct ShortcutCandidate(
        string action,
        ResolvedAction resolved,
        HotKeyGesture gesture);
}
