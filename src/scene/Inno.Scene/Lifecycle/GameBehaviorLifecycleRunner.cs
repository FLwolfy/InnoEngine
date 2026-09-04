using System.Collections.Generic;

namespace Inno.Scene;

/// <summary>
/// Dispatches GameBehavior lifecycle messages from stable scene snapshots.
/// </summary>
internal sealed class GameBehaviorLifecycleRunner
{
    private readonly GameScene m_scene;
    private readonly HashSet<GameBehavior> m_pendingActivationSet = [];
    private readonly HashSet<GameBehavior> m_pendingStartSet = [];
    private readonly List<GameBehavior> m_pendingActivation = [];
    private readonly List<GameBehavior> m_pendingStart = [];
    private GameBehavior[] m_activationBehaviors = [];
    private GameBehavior[] m_variableFrameBehaviors = [];
    private GameBehavior[] m_updateBehaviors = [];
    private GameBehavior[] m_fixedUpdateBehaviors = [];
    private GameBehavior[] m_lateUpdateBehaviors = [];
    private long m_indexedSceneRevision = -1;
    private long m_indexedTypeGeneration = -1;

    internal GameBehaviorLifecycleRunner(GameScene scene)
    {
        m_scene = scene;
    }

    internal void FixedUpdate()
    {
        EnsureIndexes();
        PreparePendingActivation();
        IReadOnlyList<GameBehavior> behaviors = m_fixedUpdateBehaviors;
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (behavior.isActiveAndEnabled)
                behavior.DispatchFixedUpdate();
        }
    }

    internal void Update()
    {
        EnsureIndexes();
        PreparePendingActivation();
        DispatchPendingStart();
        IReadOnlyList<GameBehavior> behaviors = m_updateBehaviors;
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (!behavior.isActiveAndEnabled || !behavior.lifecycleStartCalled)
                continue;
            behavior.DispatchUpdate();
        }
    }

    internal void LateUpdate()
    {
        EnsureIndexes();
        PreparePendingActivation();
        IReadOnlyList<GameBehavior> behaviors = m_lateUpdateBehaviors;
        for (int i = 0; i < behaviors.Count; i++)
        {
            if (!m_scene.canDispatch)
                break;
            GameBehavior behavior = behaviors[i];
            if (behavior.isActiveAndEnabled && behavior.lifecycleStartCalled)
                behavior.DispatchLateUpdate();
        }
    }

    internal void Refresh(GameBehavior behavior)
    {
        EnsureIndexes();
        GameBehaviorLifecyclePhase phases = GetPhases(behavior);
        if ((phases & GameBehaviorLifecyclePhase.Activation) != 0)
            SynchronizeActivation(behavior);
        UpdateStartEligibility(behavior, phases);
    }

    internal void RefreshAll()
    {
        EnsureIndexes();
        for (int index = 0; index < m_activationBehaviors.Length; index++)
            SynchronizeActivation(m_activationBehaviors[index]);
        for (int index = 0; index < m_variableFrameBehaviors.Length; index++)
        {
            GameBehavior behavior = m_variableFrameBehaviors[index];
            UpdateStartEligibility(behavior, GetPhases(behavior));
        }
    }

    internal void Destroy(GameBehavior behavior)
    {
        GameBehaviorLifecyclePhase phases = GetPhases(behavior);
        if ((phases & GameBehaviorLifecyclePhase.Activation) != 0)
            SceneLifecycle.Destroy(behavior);
    }

    internal void Invalidate()
    {
        m_activationBehaviors = [];
        m_variableFrameBehaviors = [];
        m_updateBehaviors = [];
        m_fixedUpdateBehaviors = [];
        m_lateUpdateBehaviors = [];
        m_pendingActivation.Clear();
        m_pendingActivationSet.Clear();
        m_pendingStart.Clear();
        m_pendingStartSet.Clear();
        m_indexedSceneRevision = -1;
        m_indexedTypeGeneration = -1;
    }

    private void PreparePendingActivation()
    {
        if (m_pendingActivation.Count == 0)
            return;

        GameBehavior[] pending = [.. m_pendingActivation];
        m_pendingActivation.Clear();
        m_pendingActivationSet.Clear();
        for (int index = 0; index < pending.Length; index++)
        {
            if (!m_scene.canDispatch)
            {
                for (; index < pending.Length; index++)
                    QueueActivation(pending[index]);
                break;
            }

            GameBehavior behavior = pending[index];
            if (!NeedsActivationPreparation(behavior))
                continue;
            _ = SceneLifecycle.Prepare(behavior, m_scene);
            if (NeedsActivationPreparation(behavior))
                QueueActivation(behavior);
        }
    }

    private void DispatchPendingStart()
    {
        if (m_pendingStart.Count == 0)
            return;

        GameBehavior[] pending = [.. m_pendingStart];
        m_pendingStart.Clear();
        m_pendingStartSet.Clear();
        for (int index = 0; index < pending.Length; index++)
        {
            if (!m_scene.canDispatch)
            {
                for (; index < pending.Length; index++)
                    UpdateStartEligibility(pending[index], GetPhases(pending[index]));
                break;
            }

            GameBehavior behavior = pending[index];
            if (behavior.isDestroyed || behavior.lifecycleStartCalled || !behavior.isActiveAndEnabled)
                continue;
            behavior.lifecycleStartCalled = true;
            behavior.DispatchStart();
        }
    }

    private void EnsureIndexes()
    {
        long sceneRevision = m_scene.structureRevision;
        long typeGeneration = m_scene.typeCatalog.generation;
        if (m_indexedSceneRevision == sceneRevision && m_indexedTypeGeneration == typeGeneration)
            return;

        IReadOnlyList<GameBehavior> behaviors = m_scene.GetComponents<GameBehavior>();
        var activation = new List<GameBehavior>();
        var variableFrame = new List<GameBehavior>();
        var update = new List<GameBehavior>();
        var fixedUpdate = new List<GameBehavior>();
        var lateUpdate = new List<GameBehavior>();
        for (int index = 0; index < behaviors.Count; index++)
        {
            GameBehavior behavior = behaviors[index];
            GameBehaviorLifecyclePhase phases = GetPhases(behavior);
            if (phases == GameBehaviorLifecyclePhase.None)
                continue;
            if ((phases & GameBehaviorLifecyclePhase.Activation) != 0)
                activation.Add(behavior);
            if ((phases & GameBehaviorLifecyclePhase.VariableFrame) != 0)
                variableFrame.Add(behavior);
            if ((phases & GameBehaviorLifecyclePhase.Update) != 0)
                update.Add(behavior);
            if ((phases & GameBehaviorLifecyclePhase.FixedUpdate) != 0)
                fixedUpdate.Add(behavior);
            if ((phases & GameBehaviorLifecyclePhase.LateUpdate) != 0)
                lateUpdate.Add(behavior);
        }

        m_pendingActivation.Clear();
        m_pendingActivationSet.Clear();
        m_pendingStart.Clear();
        m_pendingStartSet.Clear();
        m_activationBehaviors = [.. activation];
        m_variableFrameBehaviors = [.. variableFrame];
        m_updateBehaviors = [.. update];
        m_fixedUpdateBehaviors = [.. fixedUpdate];
        m_lateUpdateBehaviors = [.. lateUpdate];
        for (int index = 0; index < m_activationBehaviors.Length; index++)
        {
            GameBehavior behavior = m_activationBehaviors[index];
            if (NeedsActivationPreparation(behavior))
                QueueActivation(behavior);
        }
        for (int index = 0; index < m_variableFrameBehaviors.Length; index++)
        {
            GameBehavior behavior = m_variableFrameBehaviors[index];
            UpdateStartEligibility(behavior, GetPhases(behavior));
        }
        m_indexedSceneRevision = sceneRevision;
        m_indexedTypeGeneration = typeGeneration;
    }

    private void SynchronizeActivation(GameBehavior behavior)
    {
        if (!m_scene.canDispatch)
        {
            QueueActivation(behavior);
            return;
        }

        _ = SceneLifecycle.Prepare(behavior, m_scene);
        if (NeedsActivationPreparation(behavior))
            QueueActivation(behavior);
        else
            m_pendingActivationSet.Remove(behavior);
    }

    private void UpdateStartEligibility(
        GameBehavior behavior,
        GameBehaviorLifecyclePhase phases)
    {
        if ((phases & GameBehaviorLifecyclePhase.VariableFrame) == 0 ||
            behavior.isDestroyed ||
            behavior.lifecycleStartCalled ||
            !behavior.isActiveAndEnabled)
        {
            m_pendingStartSet.Remove(behavior);
            return;
        }

        if (m_pendingStartSet.Add(behavior))
            m_pendingStart.Add(behavior);
    }

    private void QueueActivation(GameBehavior behavior)
    {
        if (!behavior.isDestroyed && m_pendingActivationSet.Add(behavior))
            m_pendingActivation.Add(behavior);
    }

    private static bool NeedsActivationPreparation(GameBehavior behavior)
    {
        if (behavior.isDestroyed || behavior.lifecycleDestroyCalled)
            return false;
        bool active = behavior.isActiveAndEnabled;
        return (active && !behavior.lifecycleAwakeCalled) || active != behavior.lifecycleWasEnabled;
    }

    private GameBehaviorLifecyclePhase GetPhases(GameBehavior behavior)
        => behavior.lifecyclePhases;
}
