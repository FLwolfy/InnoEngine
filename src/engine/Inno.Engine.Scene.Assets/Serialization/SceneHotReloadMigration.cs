using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Engine.Scene.Assets;

internal sealed class SceneHotReloadMigration : ISceneReloadMigration
{
    private readonly TypeCacheReloadContext m_context;
    private readonly List<SceneState> m_scenes;
    private readonly List<Replacement> m_replacements = [];
    private readonly List<SceneReloadDiagnostic> m_diagnostics = [];
    private bool m_applied;
    private bool m_finished;

    private SceneHotReloadMigration(TypeCacheReloadContext context, List<SceneState> scenes)
    {
        m_context = context;
        m_scenes = scenes;
    }

    internal IReadOnlyList<object> retiredObjects => m_scenes
        .SelectMany(static scene => scene.states)
        .Where(state => m_context.IsRetiredType(state.target.GetType()))
        .Select(static state => (object)state.target)
        .ToArray();

    internal IReadOnlyList<SceneReloadDiagnostic> diagnostics => m_diagnostics;

    internal static SceneHotReloadMigration Capture(TypeCacheReloadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scenes = new List<SceneState>();
        foreach (GameScene scene in SceneManager.loadedScenes)
        {
            SceneStructureSnapshot structure = scene.CaptureStructure();
            EngineObject[] engineObjects = structure.objects
                .SelectMany(static entry => entry.components.Cast<EngineObject>().Prepend(entry.gameObject))
                .Concat(scene.GetSystems())
                .ToArray();
            var sourceIds = new Dictionary<EngineObject, Guid>(ReferenceEqualityComparer.Instance);
            foreach (EngineObject target in engineObjects)
                sourceIds.Add(target, target.identity.persistentId);
            var references = new SceneGraphReferenceMap(scene, engineObjects, sourceIds);
            var states = new List<ObjectState>();
            using (references.Enter())
            {
                foreach (GameComponent component in structure.objects.SelectMany(static entry => entry.components))
                    states.Add(CaptureState(component, context.previous));
                foreach (GameSystem system in scene.GetSystems())
                    states.Add(CaptureState(system, context.previous));
            }

            foreach (ObjectState state in states.Where(state => context.IsRetiredType(state.target.GetType())))
            {
                if (!context.TryResolveReplacement(state.target.GetType(), out Type? replacementType) || replacementType is null)
                {
                    _ = context.previous.TryGetStableTypeId(
                        state.target.GetType(),
                        out Guid stableTypeId);
                    throw new InvalidOperationException(
                        $"Live type '{state.target.GetType().FullName}' with StableTypeId " +
                        $"'{stableTypeId:D}' has no replacement in the candidate assembly generation.");
                }
                Type requiredBase = state.target is GameSystem ? typeof(GameSystem) : typeof(GameComponent);
                if (!requiredBase.IsAssignableFrom(replacementType) || replacementType.IsAbstract)
                {
                    throw new InvalidOperationException(
                        $"Replacement '{replacementType.FullName}' is not a concrete {requiredBase.Name}.");
                }
            }
            ValidateMultiplicity(scene, structure, context);
            scenes.Add(new SceneState(scene, engineObjects, sourceIds, states));
        }
        return new SceneHotReloadMigration(context, scenes);
    }

    internal void PrepareForActivation()
    {
        EnsureActive();
        foreach (ObjectState state in RetiredStates())
        {
            if (state.target is ISceneLifecycleObject lifecycle)
                SceneLifecycle.DisableForReload(lifecycle);
        }
    }

    internal void Apply()
    {
        EnsureActive();
        if (m_applied)
            return;
        foreach (SceneState sceneState in m_scenes)
        {
            foreach (ObjectState state in sceneState.states)
            {
                if (!m_context.IsRetiredType(state.target.GetType()))
                    continue;
                _ = m_context.TryResolveReplacement(state.target.GetType(), out Type? replacementType);
                if (!m_context.candidate.TryGetRuntimeTypeId(replacementType!, out int replacementRuntimeTypeId))
                {
                    throw new InvalidOperationException(
                        $"Replacement type '{replacementType!.FullName}' has no candidate runtime type identity.");
                }
                EngineObject replacement = CreateReplacement(state.target, replacementType!);
                CopyLifecycle(state.target, replacement);
                if (state.target is GameComponent previousComponent)
                {
                    sceneState.scene.ReplaceComponentForReload(
                        previousComponent,
                        (GameComponent)replacement,
                        replacementRuntimeTypeId);
                }
                else
                {
                    sceneState.scene.ReplaceSystemForReload(
                        (GameSystem)state.target,
                        (GameSystem)replacement,
                        replacementRuntimeTypeId);
                }
                state.currentTarget = replacement;
                m_replacements.Add(new Replacement(
                    sceneState.scene,
                    state.target,
                    replacement,
                    state.runtimeTypeId));
            }
            RestoreState(sceneState, useCurrentTargets: true);
        }
        m_applied = true;
    }

    internal void RollbackStructure()
    {
        if (m_finished)
            return;
        for (int i = m_replacements.Count - 1; i >= 0; i--)
        {
            Replacement replacement = m_replacements[i];
            if (replacement.previous is GameComponent previousComponent)
            {
                replacement.scene.ReplaceComponentForReload(
                    (GameComponent)replacement.current,
                    previousComponent,
                    replacement.previousRuntimeTypeId);
            }
            else
            {
                replacement.scene.ReplaceSystemForReload(
                    (GameSystem)replacement.current,
                    (GameSystem)replacement.previous,
                    replacement.previousRuntimeTypeId);
            }
        }
        foreach (SceneState sceneState in m_scenes)
        {
            foreach (ObjectState state in sceneState.states)
                state.currentTarget = state.target;
        }
        m_replacements.Clear();
        m_applied = false;
    }

    internal void RestorePreviousState()
    {
        EnsureActive();
        foreach (SceneState sceneState in m_scenes)
            RestoreState(sceneState, useCurrentTargets: false);
    }

    internal void Complete()
    {
        EnsureActive();
        foreach (Replacement replacement in m_replacements)
        {
            if (replacement.previous is GameComponent component && !component.isDestroyed)
                component.Detach();
            else if (replacement.previous is GameSystem system && !system.isDestroyed)
                system.Detach();
        }
        m_replacements.Clear();
        m_scenes.Clear();
        m_finished = true;
    }

    IReadOnlyList<object> ISceneReloadMigration.retiredObjects => retiredObjects;
    IReadOnlyList<SceneReloadDiagnostic> ISceneReloadMigration.diagnostics => diagnostics;
    void ISceneReloadMigration.PrepareForActivation() => PrepareForActivation();
    void ISceneReloadMigration.Apply() => Apply();
    void ISceneReloadMigration.RollbackStructure() => RollbackStructure();
    void ISceneReloadMigration.RestorePreviousState() => RestorePreviousState();
    void ISceneReloadMigration.Complete() => Complete();

    private static ObjectState CaptureState(EngineObject target, TypeCacheSnapshot types)
    {
        if (!types.TryGetRuntimeTypeId(target.GetType(), out int runtimeTypeId))
        {
            throw new InvalidOperationException(
                $"Scene object type '{target.GetType().FullName}' has no runtime identity in the previous TypeCache generation.");
        }
        var serializable = (ISerializable)target;
        IReadOnlyList<SerializationPropertySnapshot> properties =
            SerializationManager.CaptureProperties(serializable);
        return new ObjectState(target, runtimeTypeId, properties);
    }

    private static EngineObject CreateReplacement(EngineObject previous, Type replacementType)
    {
        try
        {
            return previous is GameComponent
                ? ComponentFactory.Create(replacementType)
                : (EngineObject)(Activator.CreateInstance(replacementType, nonPublic: true)
                    ?? throw new InvalidOperationException("Activator returned null."));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Hot-reload replacement '{replacementType.FullName}' requires a parameterless constructor.",
                exception);
        }
    }

    private static void CopyLifecycle(EngineObject previous, EngineObject replacement)
    {
        if (previous is GameBehavior previousBehavior && replacement is GameBehavior replacementBehavior)
        {
            replacementBehavior.enabled = previousBehavior.enabled;
            replacementBehavior.lifecycleAwakeCalled = previousBehavior.lifecycleAwakeCalled;
            replacementBehavior.lifecycleStartCalled = previousBehavior.lifecycleStartCalled;
            replacementBehavior.lifecycleWasEnabled = false;
            return;
        }
        if (previous is GameSystem previousSystem && replacement is GameSystem replacementSystem)
        {
            replacementSystem.enabled = previousSystem.enabled;
            replacementSystem.lifecycleAwakeCalled = previousSystem.lifecycleAwakeCalled;
            replacementSystem.lifecycleStartCalled = previousSystem.lifecycleStartCalled;
            replacementSystem.lifecycleWasEnabled = false;
        }
    }

    private static void ValidateMultiplicity(
        GameScene scene,
        SceneStructureSnapshot structure,
        TypeCacheReloadContext context)
    {
        foreach (SceneObjectStructureSnapshot entry in structure.objects)
        {
            var groups = new Dictionary<int, MultiplicityGroup>();
            foreach (GameComponent component in entry.components)
            {
                ActiveTypeInfo type = ResolveActiveType(component.GetType(), context, isSystem: false);
                if (!groups.TryGetValue(type.runtimeTypeId, out MultiplicityGroup? group))
                    groups.Add(type.runtimeTypeId, new MultiplicityGroup(type.displayName, type.allowsMultiple));
                else
                    group.count++;
            }
            foreach (MultiplicityGroup group in groups.Values)
            {
                if (group.count <= 1 || group.allowsMultiple)
                    continue;
                throw new InvalidOperationException(
                    $"Script reload would leave GameObject '{entry.gameObject.name}' " +
                    $"({entry.gameObject.identity.persistentId}) with {group.count} instances of unique component " +
                    $"'{group.displayName}'. Remove duplicate components or restore " +
                    $"[{nameof(AllowMultipleComponentAttribute)}] before reloading.");
            }
        }

        var systemGroups = new Dictionary<int, MultiplicityGroup>();
        foreach (GameSystem system in scene.GetSystems())
        {
            ActiveTypeInfo type = ResolveActiveType(system.GetType(), context, isSystem: true);
            if (!systemGroups.TryGetValue(type.runtimeTypeId, out MultiplicityGroup? group))
                systemGroups.Add(type.runtimeTypeId, new MultiplicityGroup(type.displayName, type.allowsMultiple));
            else
                group.count++;
        }
        foreach (MultiplicityGroup group in systemGroups.Values)
        {
            if (group.count <= 1 || group.allowsMultiple)
                continue;
            throw new InvalidOperationException(
                $"Script reload would leave scene '{scene.name}' ({scene.identity.persistentId}) with " +
                $"{group.count} instances of unique system '{group.displayName}'. Remove duplicate systems or restore " +
                $"[{nameof(AllowMultipleSystemAttribute)}] before reloading.");
        }
    }

    private static ActiveTypeInfo ResolveActiveType(
        Type type,
        TypeCacheReloadContext context,
        bool isSystem)
    {
        Type activeType = context.IsRetiredType(type) &&
                          context.TryResolveReplacement(type, out Type? replacement) &&
                          replacement is not null
            ? replacement
            : type;
        if (!context.candidate.TryGetRuntimeTypeId(activeType, out int runtimeTypeId))
        {
            throw new InvalidOperationException(
                $"Active scene type '{activeType.FullName}' has no candidate runtime type identity.");
        }
        bool allowsMultiple = activeType.IsDefined(
            isSystem ? typeof(AllowMultipleSystemAttribute) : typeof(AllowMultipleComponentAttribute),
            inherit: !isSystem);
        return new ActiveTypeInfo(
            runtimeTypeId,
            activeType.FullName ?? activeType.Name,
            allowsMultiple);
    }

    private void RestoreState(SceneState sceneState, bool useCurrentTargets)
    {
        EngineObject[] currentObjects = sceneState.engineObjects
            .Select(engineObject => sceneState.states.FirstOrDefault(state => ReferenceEquals(state.target, engineObject))
                is ObjectState state
                    ? useCurrentTargets ? state.currentTarget : state.target
                    : engineObject)
            .ToArray();
        var references = new SceneGraphReferenceMap(sceneState.scene);
        for (int i = 0; i < currentObjects.Length; i++)
            references.Register(sceneState.engineObjects[i].identity.persistentId, currentObjects[i]);
        using (references.Enter())
        {
            foreach (ObjectState state in sceneState.states)
            {
                ISerializable target = (ISerializable)(useCurrentTargets ? state.currentTarget : state.target);
                SerializationPropertyRestoreResult result = SerializationManager.RestoreProperties(
                    target,
                    state.properties,
                    useCurrentTargets
                        ? SerializationPropertyRestoreMode.Compatible
                        : SerializationPropertyRestoreMode.Strict);
                if (!useCurrentTargets)
                    continue;
                for (int i = 0; i < result.failures.Count; i++)
                {
                    SerializationPropertyRestoreFailure failure = result.failures[i];
                    string previousType = GetTypeDisplayName(failure.previousPropertyType);
                    string currentType = GetTypeDisplayName(failure.currentPropertyType);
                    m_diagnostics.Add(new SceneReloadDiagnostic(
                        "INNOHR0001",
                        SceneReloadDiagnosticSeverity.Warning,
                        $"Hot reload skipped '{target.GetType().FullName}.{failure.name}' because " +
                        $"serialized type '{previousType}' is incompatible with '{currentType}'. " +
                        $"The new member default value was preserved. {failure.message}",
                        sceneState.scene.identity.persistentId,
                        ((EngineObject)target).identity.persistentId,
                        failure.name,
                        previousType,
                        currentType));
                }
            }
        }
    }

    private static string GetTypeDisplayName(Type type)
        => type.FullName ?? type.Name;

    private IEnumerable<ObjectState> RetiredStates()
        => m_scenes.SelectMany(static scene => scene.states)
            .Where(state => m_context.IsRetiredType(state.target.GetType()));

    private void EnsureActive()
    {
        if (m_finished)
            throw new InvalidOperationException("Scene hot-reload migration is already finished.");
    }

    private sealed record SceneState(
        GameScene scene,
        EngineObject[] engineObjects,
        IReadOnlyDictionary<EngineObject, Guid> sourceIds,
        List<ObjectState> states);

    private sealed class ObjectState(
        EngineObject target,
        int runtimeTypeId,
        IReadOnlyList<SerializationPropertySnapshot> properties)
    {
        internal EngineObject target { get; } = target;
        internal int runtimeTypeId { get; } = runtimeTypeId;
        internal IReadOnlyList<SerializationPropertySnapshot> properties { get; } = properties;
        internal EngineObject currentTarget { get; set; } = target;
    }

    private sealed record Replacement(
        GameScene scene,
        EngineObject previous,
        EngineObject current,
        int previousRuntimeTypeId);

    private sealed class MultiplicityGroup(string displayName, bool allowsMultiple)
    {
        internal string displayName { get; } = displayName;
        internal bool allowsMultiple { get; } = allowsMultiple;
        internal int count { get; set; } = 1;
    }

    private readonly record struct ActiveTypeInfo(
        int runtimeTypeId,
        string displayName,
        bool allowsMultiple);
}
