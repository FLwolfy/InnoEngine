using System;
using System.Collections.Generic;

using Inno.Core.Events;
using Inno.Core.Logging;
using Inno.Editor.Core.Commands;
using Inno.Editor.Interactions.Internal;

namespace Inno.Editor.Interactions.Commands;

internal sealed class EditorActionRouter(
    EditorExtensionCatalog catalog,
    Inno.Editor.Core.EditorContext editorContext)
{
    private readonly Queue<(string Id, EditorActionContext Context)> m_pending = [];

    internal EditorActionState Query(string actionId, EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(actionId, context);
        if (registration is null)
            return EditorActionState.hidden;
        try
        {
            return registration.action.Query(context);
        }
        catch (Exception exception)
        {
            Log.Error("Editor action '{0}' query failed: {1}", actionId, exception);
            return EditorActionState.disabled;
        }
    }

    internal bool Execute(string actionId, EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(actionId, context);
        if (registration is null)
            return false;
        try
        {
            EditorActionState state = registration.action.Query(context);
            if (!state.isVisible || !state.isEnabled)
                return false;
            registration.action.Execute(context);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("Editor action '{0}' failed: {1}", actionId, exception);
            return false;
        }
    }

    internal void Enqueue(string actionId, EditorActionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(context);
        m_pending.Enqueue((actionId, context));
    }

    internal void Flush()
    {
        while (m_pending.TryDequeue(out (string Id, EditorActionContext Context) request))
            _ = Execute(request.Id, request.Context);
    }

    internal void Clear() => m_pending.Clear();

    internal bool TryGetInteraction<TState>(
        string actionId,
        EditorActionContext context,
        out EditorActionInteraction<TState>? interaction)
    {
        EditorExtensionCatalog.ActionRegistration? registration = Resolve(actionId, context);
        if (registration is null)
        {
            interaction = null;
            return false;
        }
        return registration.action.TryGetInteraction(context.target, out interaction);
    }

    internal bool TryGetShortcut(
        string actionId,
        Type surface,
        out HotKeyGesture gesture)
    {
        EditorShortcutAttribute? best = null;
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            if (!string.Equals(registration.attribute.id, actionId, StringComparison.Ordinal))
                continue;
            foreach (EditorShortcutAttribute shortcut in registration.shortcuts)
            {
                if (shortcut.surface is not null && shortcut.surface != surface)
                    continue;
                if (best is null || best.surface is null && shortcut.surface is not null)
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

    internal bool DispatchShortcut(KeyPressedEvent keyEvent, Type surface, object? target)
    {
        var handledActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            if (!handledActions.Add(registration.attribute.id))
                continue;
            foreach (EditorShortcutAttribute shortcut in registration.shortcuts)
            {
                if (shortcut.surface is not null && shortcut.surface != surface)
                    continue;
                if (!CreateGesture(shortcut).Matches(keyEvent))
                    continue;
                var context = new EditorActionContext(
                    editorContext,
                    surface,
                    target);
                if (Execute(registration.attribute.id, context))
                    return true;
            }
        }
        return false;
    }

    private EditorExtensionCatalog.ActionRegistration? Resolve(
        string actionId,
        EditorActionContext context)
    {
        EditorExtensionCatalog.ActionRegistration? best = null;
        int bestDistance = int.MaxValue;
        foreach (EditorExtensionCatalog.ActionRegistration registration in catalog.extensions.actions)
        {
            if (!string.Equals(registration.attribute.id, actionId, StringComparison.Ordinal))
                continue;
            bool exactSurface = registration.attribute.surface is not null;
            if (exactSurface && registration.attribute.surface != context.surface)
                continue;

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

            if (best is null ||
                exactSurface && best.attribute.surface is null ||
                exactSurface == (best.attribute.surface is not null) &&
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
