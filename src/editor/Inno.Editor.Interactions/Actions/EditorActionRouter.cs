using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Interactions;

namespace Inno.Editor.Interactions.Actions;

internal sealed class EditorActionRouter(
    EditorExtensionCatalog catalog,
    EditorContext editor,
    EditorInteractions interactions)
{
    private readonly Queue<(string Action, EditorActionContext Context)> m_pending = [];

    internal EditorActionState Query(string action, EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(action, context);
        if (registration is null)
            return EditorActionState.hidden;
        try
        {
            return registration.action.QueryInternal(context);
        }
        catch (Exception exception)
        {
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
            return registration.action.PresentInternal(context);
        }
        catch (Exception exception)
        {
            registration.action.CancelInternal();
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
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
            registration.action.CancelInternal();
    }

    internal bool TryGetShortcut(string action, string area, out HotKeyGesture gesture)
    {
        EditorShortcutAttribute? best = null;
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            if (!string.Equals(registration.attribute.action, action, StringComparison.Ordinal))
                continue;
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

    internal bool DispatchShortcut(KeyPressedEvent keyEvent, string area, object? target)
    {
        var handledActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            string action = registration.attribute.action;
            if (!handledActions.Add(action))
                continue;
            foreach (EditorShortcutAttribute shortcut in registration.shortcuts)
            {
                if (!string.IsNullOrEmpty(shortcut.area) &&
                    !string.Equals(shortcut.area, area, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!CreateGesture(shortcut).Matches(keyEvent))
                    continue;
                var context = new EditorActionContext(editor, interactions, area, target);
                if (Execute(action, context))
                    return true;
            }
        }
        return false;
    }

    private EditorExtensionCatalog.ActionRegistration? Resolve(
        string action,
        EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? best = null;
        int bestDistance = int.MaxValue;
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            if (!string.Equals(registration.attribute.action, action, StringComparison.Ordinal))
                continue;
            bool exactArea = !string.IsNullOrEmpty(registration.attribute.area);
            if (exactArea &&
                !string.Equals(registration.attribute.area, context.area, StringComparison.Ordinal))
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

            bool bestExactArea = best is not null && !string.IsNullOrEmpty(best.attribute.area);
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
        return best;
    }

    private static HotKeyGesture CreateGesture(EditorShortcutAttribute shortcut)
        => shortcut.primary
            ? HotKeyGesture.Primary(shortcut.key, shortcut.modifiers)
            : new HotKeyGesture(shortcut.key, shortcut.modifiers);
}
