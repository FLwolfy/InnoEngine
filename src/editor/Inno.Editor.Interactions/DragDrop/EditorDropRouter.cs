using System;
using System.Collections.Generic;

using Inno.Core.Logging;

namespace Inno.Editor.Interactions;

internal sealed class EditorDropRouter(EditorExtensionCatalog catalog)
{
    private readonly HashSet<string> m_queryFailures = new(StringComparer.Ordinal);
    private Guid m_token;
    private EditorDragData? m_data;

    internal Guid Begin(EditorDragContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (m_data is null || !Equals(m_data.source, context.data.source))
        {
            m_token = Guid.NewGuid();
            m_data = context.data;
        }
        return m_token;
    }

    internal bool TryGetData(Guid token, out EditorDragData? data)
    {
        data = token != Guid.Empty && token == m_token ? m_data : null;
        if (data is not null && !data.isValid)
        {
            Cancel();
            data = null;
        }
        return data is not null;
    }

    internal EditorDropStatus Query(Guid token, EditorDropContext context)
    {
        if (!TryResolve(token, context, out EditorExtensionCatalog.DropRegistration? registration) ||
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
                Log.Error("Editor drop query failed: {0}", exception);
            return EditorDropStatus.rejected;
        }
    }

    internal EditorDropResult Drop(Guid token, EditorDropContext context)
    {
        if (!TryResolve(token, context, out EditorExtensionCatalog.DropRegistration? registration) ||
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
            Log.Error("Editor drop failed: {0}", exception);
            return EditorDropResult.rejected;
        }
    }

    internal void Cancel()
    {
        m_token = Guid.Empty;
        m_data = null;
        m_queryFailures.Clear();
    }

    private bool TryResolve(
        Guid token,
        EditorDropContext context,
        out EditorExtensionCatalog.DropRegistration? best)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryGetData(token, out EditorDragData? data) || !ReferenceEquals(data, context.data))
        {
            best = null;
            return false;
        }

        Type sourceType = context.data.source.GetType();
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
}
