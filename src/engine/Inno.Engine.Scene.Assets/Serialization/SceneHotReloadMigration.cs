using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Assets.Serialization;
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
                Type? replacementType = ResolveCandidateType(state, m_context);
                bool retired = m_context.IsRetiredType(state.target.GetType());
                bool recovering = state.target is MissingGameComponent or MissingGameSystem &&
                                  replacementType is not null;
                if (!retired && !recovering)
                    continue;

                Type effectiveType = replacementType ?? (state.target is GameSystem
                    ? typeof(MissingGameSystem)
                    : typeof(MissingGameComponent));
                if (!m_context.candidate.TryGetRuntimeTypeId(effectiveType, out int replacementRuntimeTypeId))
                {
                    throw new InvalidOperationException(
                        $"Replacement type '{effectiveType.FullName}' has no candidate runtime type identity.");
                }
                EngineObject replacement = CreateReplacement(state, effectiveType);
                if (replacement is not MissingGameComponent and not MissingGameSystem)
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
                if (replacementType is null || recovering)
                {
                    m_diagnostics.Add(new SceneReloadDiagnostic(
                        replacementType is null ? "INNOHR0002" : "INNOHR0003",
                        SceneReloadDiagnosticSeverity.Warning,
                        replacementType is null
                            ? $"'{state.typeName}' is unavailable. Its identity and serialized state are preserved as a missing scene element."
                            : $"Missing scene element '{state.typeName}' recovered as '{replacementType.FullName}'.",
                        sceneState.scene.identity.persistentId,
                        replacement.identity.persistentId,
                        string.Empty,
                        state.typeName,
                        replacementType?.FullName ?? string.Empty));
                }
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
        List<Exception>? failures = null;
        foreach (SceneState sceneState in m_scenes)
        {
            try
            {
                RestoreState(sceneState, useCurrentTargets: false);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
            RestorePreviousLifecycleState(sceneState, ref failures);
        }
        if (failures is not null)
            throw new AggregateException("Previous scene state restoration was incomplete.", failures);
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
        if (target is MissingGameComponent missingComponent)
        {
            return new ObjectState(
                target,
                runtimeTypeId,
                missingComponent.missingTypeId,
                missingComponent.missingTypeName,
                missingComponent.CaptureSerializedState(),
                missingComponent.dependencies.ToArray());
        }
        if (target is MissingGameSystem missingSystem)
        {
            return new ObjectState(
                target,
                runtimeTypeId,
                missingSystem.missingTypeId,
                missingSystem.missingTypeName,
                missingSystem.CaptureSerializedState(),
                missingSystem.dependencies.ToArray());
        }
        if (!types.TryGetStableTypeId(target.GetType(), out Guid stableTypeId))
        {
            throw new InvalidOperationException(
                $"Scene object type '{target.GetType().FullName}' has no stable identity in the previous TypeCache generation.");
        }
        var dependencies = new AssetDependencyCollection();
        byte[] data = SerializationManager.CapturePropertiesData(
            (ISerializable)target,
            SerializationContext.empty.With(dependencies));
        return new ObjectState(
            target,
            runtimeTypeId,
            stableTypeId,
            target.GetType().FullName ?? target.GetType().Name,
            data,
            dependencies.dependencies.ToArray());
    }

    private static void RestorePreviousLifecycleState(
        SceneState sceneState,
        ref List<Exception>? failures)
    {
        foreach (ObjectState state in sceneState.states)
        {
            if (state.target is not ISceneLifecycleObject lifecycle ||
                lifecycle.lifecycleWasEnabled == state.lifecycleWasEnabled)
            {
                continue;
            }

            lifecycle.lifecycleWasEnabled = state.lifecycleWasEnabled;
            try
            {
                if (state.lifecycleWasEnabled)
                    lifecycle.DispatchEnable();
                else
                    lifecycle.DispatchDisable();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(new InvalidOperationException(
                    $"Lifecycle compensation failed for '{state.target.GetType().FullName}'.",
                    exception));
            }
        }
    }

    private static EngineObject CreateReplacement(ObjectState state, Type replacementType)
    {
        if (replacementType == typeof(MissingGameComponent))
            return new MissingGameComponent(
                state.stableTypeId,
                state.typeName,
                state.data,
                state.dependencies);
        if (replacementType == typeof(MissingGameSystem))
            return new MissingGameSystem(
                state.stableTypeId,
                state.typeName,
                state.data,
                state.dependencies);
        try
        {
            return state.target is GameComponent
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
                ActiveTypeInfo type = ResolveActiveType(component, context);
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
            ActiveTypeInfo type = ResolveActiveType(system, context);
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
        EngineObject target,
        TypeCacheReloadContext context)
    {
        bool isSystem = target is GameSystem;
        Type type = target.GetType();
        Type activeType = type;
        if (target is MissingGameComponent missingComponent)
        {
            activeType = context.candidate.TryResolveType(missingComponent.missingTypeId, out Type? recovered) &&
                         recovered is not null
                ? recovered
                : typeof(MissingGameComponent);
        }
        else if (target is MissingGameSystem missingSystem)
        {
            activeType = context.candidate.TryResolveType(missingSystem.missingTypeId, out Type? recovered) &&
                         recovered is not null
                ? recovered
                : typeof(MissingGameSystem);
        }
        else if (context.IsRetiredType(type))
        {
            activeType = context.TryResolveReplacement(type, out Type? replacement) && replacement is not null
                ? replacement
                : isSystem ? typeof(MissingGameSystem) : typeof(MissingGameComponent);
        }
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
        var currentByPersistentId = new Dictionary<Guid, EngineObject>();
        for (int i = 0; i < currentObjects.Length; i++)
        {
            references.Register(sceneState.engineObjects[i].identity.persistentId, currentObjects[i]);
            currentByPersistentId[currentObjects[i].identity.persistentId] = currentObjects[i];
        }
        foreach (ObjectState state in sceneState.states)
        {
            IReadOnlyDictionary<Guid, Guid>? aliases = state.target switch
            {
                MissingGameComponent component => component.referenceAliases,
                MissingGameSystem system => system.referenceAliases,
                _ => null
            };
            if (aliases is null)
                continue;
            foreach ((Guid alias, Guid targetPersistentId) in aliases)
            {
                if (!currentByPersistentId.TryGetValue(targetPersistentId, out EngineObject? target))
                {
                    throw new InvalidOperationException(
                        $"Missing scene state reference alias '{alias}' targets unavailable identity '{targetPersistentId}'.");
                }
                references.Register(alias, target);
            }
        }
        List<Exception>? restoreFailures = null;
        using (references.Enter())
        {
            foreach (ObjectState state in sceneState.states)
            {
                ISerializable target = (ISerializable)(useCurrentTargets ? state.currentTarget : state.target);
                if (target is MissingGameComponent or MissingGameSystem)
                    continue;
                SerializationPropertyRestoreResult result;
                try
                {
                    result = SerializationManager.RestorePropertiesData(
                        target,
                        state.data,
                        useCurrentTargets
                            ? SerializationPropertyRestoreMode.Compatible
                            : SerializationPropertyRestoreMode.Strict);
                }
                catch (Exception exception) when (!useCurrentTargets)
                {
                    restoreFailures ??= [];
                    restoreFailures.Add(new InvalidOperationException(
                        $"Previous state restoration failed for '{target.GetType().FullName}'.",
                        exception));
                    continue;
                }
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
        if (restoreFailures is not null)
            throw new AggregateException("One or more previous scene objects could not be restored.", restoreFailures);
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
        Guid stableTypeId,
        string typeName,
        byte[] data,
        AssetDependency[] dependencies)
    {
        internal EngineObject target { get; } = target;
        internal int runtimeTypeId { get; } = runtimeTypeId;
        internal Guid stableTypeId { get; } = stableTypeId;
        internal string typeName { get; } = typeName;
        internal byte[] data { get; } = data;
        internal AssetDependency[] dependencies { get; } = dependencies;
        internal bool lifecycleWasEnabled { get; } =
            target is ISceneLifecycleObject lifecycle && lifecycle.lifecycleWasEnabled;
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

    private static Type? ResolveCandidateType(ObjectState state, TypeCacheReloadContext context)
    {
        Type requiredBase = state.target is GameSystem ? typeof(GameSystem) : typeof(GameComponent);
        Type? candidate = null;
        if (state.target is MissingGameComponent or MissingGameSystem)
            _ = context.candidate.TryResolveType(state.stableTypeId, out candidate);
        else if (context.IsRetiredType(state.target.GetType()))
            _ = context.TryResolveReplacement(state.target.GetType(), out candidate);
        if (candidate is null)
            return null;
        if (!requiredBase.IsAssignableFrom(candidate) || candidate.IsAbstract ||
            candidate == typeof(MissingGameComponent) || candidate == typeof(MissingGameSystem))
        {
            throw new InvalidOperationException(
                $"Stable type id '{state.stableTypeId:D}' resolves to invalid replacement '{candidate.FullName}'.");
        }
        return candidate;
    }
}
