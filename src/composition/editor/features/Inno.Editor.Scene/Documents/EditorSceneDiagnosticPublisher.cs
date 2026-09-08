using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Diagnostics;
using Inno.Scene;

namespace Inno.Editor.Scene;

internal sealed class EditorSceneDiagnosticPublisher : IDisposable
{
    private const string C_RESTORE_GROUP = "Scene Workspace Restore";
    private const string C_SYNCHRONIZATION_GROUP = "Scene Synchronization";

    private readonly Dictionary<Guid, string> m_synchronizationStates = [];
    private string m_restoreState = string.Empty;

    internal bool PublishRestoreFailure(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        string state = $"{code}:{message}";
        if (string.Equals(m_restoreState, state, StringComparison.Ordinal))
            return false;
        Diagnostics.Set(C_RESTORE_GROUP, Diagnostic.Error(code, message));
        m_restoreState = state;
        return true;
    }

    internal void ResolveRestore()
    {
        if (string.IsNullOrEmpty(m_restoreState))
            return;
        Diagnostics.Clear(C_RESTORE_GROUP);
        m_restoreState = string.Empty;
    }

    internal bool PublishSynchronizationFailure(GameScene scene, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(exception);
        Guid targetId = scene.identity.persistentId;
        string state = exception.ToString();
        if (m_synchronizationStates.TryGetValue(targetId, out string? previous) &&
            string.Equals(previous, state, StringComparison.Ordinal))
        {
            return false;
        }
        Diagnostics.Set(
            targetId,
            C_SYNCHRONIZATION_GROUP,
            Diagnostic.Error("SCENE-SYNCHRONIZATION", exception.Message),
            scene.name);
        m_synchronizationStates[targetId] = state;
        return true;
    }

    internal void ResolveSynchronization(Guid sceneId)
    {
        if (!m_synchronizationStates.Remove(sceneId))
            return;
        Diagnostics.Clear(sceneId, C_SYNCHRONIZATION_GROUP);
    }

    internal void RetainSynchronizationTargets(IReadOnlySet<Guid> sceneIds)
    {
        ArgumentNullException.ThrowIfNull(sceneIds);
        Guid[] removed = m_synchronizationStates.Keys
            .Where(id => !sceneIds.Contains(id))
            .ToArray();
        for (int i = 0; i < removed.Length; i++)
            ResolveSynchronization(removed[i]);
    }

    /// <summary>
    /// Releases the resources owned by this instance.
    /// </summary>
    public void Dispose()
    {
        ResolveRestore();
        foreach (Guid sceneId in m_synchronizationStates.Keys.ToArray())
            ResolveSynchronization(sceneId);
    }
}
