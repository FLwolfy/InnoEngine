using System;
using System.Collections.Generic;

using Inno.Core.Identity;
using Inno.Core.Logging;

namespace Inno.Editor.Interactions;

internal sealed class EditorDropRouter(
    EditorExtensionCatalog catalog,
    IReadOnlyDictionary<IdentityDomainId, IdentityAllocator> identityDomains,
    Logger log)
{
    private readonly HashSet<string> m_queryFailures = new(StringComparer.Ordinal);
    private RuntimeIdentity? m_activeIdentity;

    internal RuntimeIdentity Begin(EditorDragContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeIdentity identity = context.data.sourceIdentity;
        if (Resolve(identity) is null)
            throw new InvalidOperationException("The editor drag source is no longer registered.");
        m_activeIdentity = identity;
        return identity;
    }

    internal bool TryGetSource(RuntimeIdentity identity, out IdentityObject? source)
    {
        source = m_activeIdentity == identity
            ? Resolve(identity)
            : null;
        if (source is not null)
            return true;
        if (m_activeIdentity == identity)
            Cancel();
        return false;
    }

    internal bool TryGetActiveIdentity(out RuntimeIdentity identity)
    {
        if (m_activeIdentity is not RuntimeIdentity active ||
            Resolve(active) is null)
        {
            Cancel();
            identity = default;
            return false;
        }
        identity = active;
        return true;
    }

    internal EditorDropStatus Query(RuntimeIdentity identity, EditorDropContext context)
    {
        if (!TryResolve(identity, context, out EditorExtensionCatalog.DropRegistration? registration) ||
            registration is null)
            return EditorDropStatus.rejected;
        try
        {
            EditorDropStatus status = registration.drop.Query(context);
            string dropName = registration.type.FullName ?? registration.type.Name;
            m_queryFailures.Remove(dropName);
            return status;
        }
        catch (Exception exception)
        {
            string dropName = registration.type.FullName ?? registration.type.Name;
            if (m_queryFailures.Add(dropName))
                log.Write(LogLevel.Error, "Editor drop query failed: {0}", [exception]);
            return EditorDropStatus.rejected;
        }
    }

    internal EditorDropResult Drop(RuntimeIdentity identity, EditorDropContext context)
    {
        if (!TryResolve(identity, context, out EditorExtensionCatalog.DropRegistration? registration) ||
            registration is null)
            return EditorDropResult.rejected;
        try
        {
            if (!registration.drop.Query(context).canDrop)
                return EditorDropResult.rejected;
            EditorDropResult result = registration.drop.Drop(context);
            if (result.accepted)
                Cancel();
            return result;
        }
        catch (Exception exception)
        {
            log.Write(LogLevel.Error, "Editor drop failed: {0}", [exception]);
            return EditorDropResult.rejected;
        }
    }

    internal void Cancel()
    {
        m_activeIdentity = null;
        m_queryFailures.Clear();
    }

    private bool TryResolve(
        RuntimeIdentity identity,
        EditorDropContext context,
        out EditorExtensionCatalog.DropRegistration? best)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryGetSource(identity, out IdentityObject? source) || !ReferenceEquals(source, context.source))
        {
            best = null;
            return false;
        }

        Type sourceType = context.source.GetType();
        Type targetType = context.target.GetType();
        best = null;
        int bestSourceDistance = int.MaxValue;
        int bestTargetDistance = int.MaxValue;
        foreach (EditorExtensionCatalog.DropRegistration registration in catalog.extensions.drops)
        {
            bool exactArea = !string.IsNullOrEmpty(registration.area);
            if (exactArea && !string.Equals(registration.area, context.area, StringComparison.Ordinal))
                continue;
            if (!EditorTypeDistance.TryGet(sourceType, registration.sourceType, out int sourceDistance) ||
                !EditorTypeDistance.TryGet(targetType, registration.targetType, out int targetDistance))
            {
                continue;
            }
            if (best is null ||
                exactArea && string.IsNullOrEmpty(best.area) ||
                exactArea == !string.IsNullOrEmpty(best.area) &&
                (sourceDistance < bestSourceDistance ||
                 sourceDistance == bestSourceDistance && targetDistance < bestTargetDistance ||
                 sourceDistance == bestSourceDistance && targetDistance == bestTargetDistance &&
                 registration.priority > best.priority))
            {
                best = registration;
                bestSourceDistance = sourceDistance;
                bestTargetDistance = targetDistance;
            }
        }
        return best is not null;
    }

    private IdentityObject? Resolve(RuntimeIdentity identity)
        => identityDomains.TryGetValue(identity.domainId, out IdentityAllocator? allocator)
            ? allocator.Get<IdentityObject>(identity)
            : null;
}
