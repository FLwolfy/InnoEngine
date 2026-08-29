using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Inno.Core.Assemblies.Internal;
using Inno.Core.Assemblies.Loading;
using Inno.Core.Storage;

namespace Inno.Core.Assemblies;

/// <summary>
/// Owns the active managed assembly catalog and transactional module generations.
/// </summary>
public static class AssemblyManager
{
    private static readonly object S_SYNC = new();
    private static readonly Dictionary<AssemblyModuleHandle, AssemblyModuleEntry> S_MODULES = [];
    private static readonly List<AssemblyUnloadMonitor> S_PENDING_UNLOADS = [];
    private static readonly HashSet<string> S_TRUSTED_PLATFORM_ASSEMBLIES = GetTrustedPlatformAssemblyNames();

    private static AssemblyManagerOptions s_options = new();
    private static AssemblyCatalogSnapshot s_currentCatalog = new(0, []);
    private static long s_catalogVersion;
    private static volatile bool s_hostCatalogDirty;
    private static bool s_catalogTransitionInProgress;
    private static bool s_rebuildPending;
    private static bool s_reloadInProgress;
    private static bool s_assemblyLoadSubscribed;

    /// <summary>
    /// Gets whether the global assembly catalog is initialized.
    /// </summary>
    public static bool isInitialized { get; private set; }

    /// <summary>
    /// Gets non-owning information about active managed and external modules.
    /// </summary>
    public static IReadOnlyList<AssemblyModuleInfo> modules
    {
        get
        {
            lock (S_SYNC)
            {
                return S_MODULES.Values
                    .OrderBy(static module => module.moduleName, StringComparer.Ordinal)
                    .Select(static module => module.CreateInfo())
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// Initializes host discovery and publishes the first assembly catalog.
    /// </summary>
    public static void Initialize(AssemblyManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.cacheDirectory))
            throw new ArgumentException("Assembly cache directory is required.", nameof(options));

        lock (S_SYNC)
        {
            if (isInitialized)
                ShutdownLocked();

            s_options = new AssemblyManagerOptions
            {
                cacheDirectory = Path.GetFullPath(options.cacheDirectory),
                preloadEntryAssemblyDependencies = options.preloadEntryAssemblyDependencies
            };
            Directory.CreateDirectory(s_options.cacheDirectory);
            CleanupRetiredShadowDirectories();
            CleanupStaleShadowDirectories();
            if (s_options.preloadEntryAssemblyDependencies)
                PreloadInnoHostDependencies();

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            s_assemblyLoadSubscribed = true;
            isInitialized = true;
            s_hostCatalogDirty = true;
            try
            {
                RebuildLocked();
            }
            catch
            {
                ShutdownLocked();
                throw;
            }
        }
    }

    /// <summary>
    /// Registers a transactional consumer of the assembly catalog and initializes it from the
    /// currently active generation.
    /// </summary>
    /// <param name="participant">The participant that derives state from catalog snapshots.</param>
    /// <returns>A registration that removes the participant when disposed.</returns>
    public static IDisposable RegisterCatalogParticipant(IAssemblyCatalogParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            CatalogParticipantRegistration registration = AssemblyCatalogCoordinator.Register(participant);
            IAssemblyCatalogTransaction? transaction = null;
            try
            {
                transaction = participant.Prepare(s_currentCatalog);
                transaction.Activate();
                try
                {
                    transaction.Complete();
                }
                catch (Exception exception)
                {
                    Trace.TraceError(
                        "Assembly catalog participant '{0}' failed during initial cleanup: {1}",
                        participant.GetType().FullName,
                        exception);
                }
                return registration;
            }
            catch
            {
                try
                {
                    transaction?.Rollback();
                }
                catch (Exception exception)
                {
                    Trace.TraceError(
                        "Assembly catalog participant '{0}' failed during initial rollback: {1}",
                        participant.GetType().FullName,
                        exception);
                }
                registration.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Loads and activates a new shadow-copied assembly module.
    /// </summary>
    public static AssemblyModuleHandle Load(AssemblyLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            ValidateUniqueModuleName(request.moduleName);
            if (request.domain != AssemblyDomain.InnoPlugin)
                ValidateUniqueReloadBoundary(request.domain, request.scope);

            AssemblyModuleHandle handle = new(Guid.NewGuid());
            IReadOnlyDictionary<string, PlannedAssembly> plannedAssemblies =
                BuildPlannedAssemblyMap([request]);
            AssemblyModuleEntry module = StageModule(
                handle,
                request,
                generation: 1,
                upstreamModules: GetUpstreamModules(request, []),
                plannedAssemblies);
            S_MODULES.Add(handle, module);
            try
            {
                RebuildLocked();
                return handle;
            }
            catch
            {
                S_MODULES.Remove(handle);
                BeginUnload(module);
                throw;
            }
        }
    }

    /// <summary>
    /// Registers assemblies owned by an external load context.
    /// </summary>
    public static AssemblyModuleHandle Register(string moduleName, IReadOnlyList<Assembly> assemblies)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name is required.", nameof(moduleName));
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Count == 0)
            throw new ArgumentException("At least one assembly is required.", nameof(assemblies));
        if (assemblies.Any(static assembly => assembly is null || assembly.IsDynamic))
            throw new ArgumentException("External modules must contain non-dynamic assemblies.", nameof(assemblies));

        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            ValidateUniqueModuleName(moduleName);

            if (!assemblies[0].TryGetInnoAssemblyClassification(
                    out AssemblyDomain domain,
                    out AssemblyScope scope) ||
                assemblies.Any(assembly =>
                    !assembly.TryGetInnoAssemblyClassification(
                        out AssemblyDomain candidateDomain,
                        out AssemblyScope candidateScope) ||
                    candidateDomain != domain ||
                    candidateScope != scope))
            {
                throw new ArgumentException(
                    "Every externally owned module assembly must declare the same valid domain and scope metadata.",
                    nameof(assemblies));
            }

            AssemblyModuleHandle handle = new(Guid.NewGuid());
            var module = new AssemblyModuleEntry
            {
                handle = handle,
                moduleName = moduleName,
                generation = 1,
                externallyOwned = true,
                collectible = false,
                domain = domain,
                scope = scope,
                assemblies = assemblies.Distinct().ToArray(),
                assemblyScopes = assemblies
                    .Distinct()
                    .ToDictionary(static assembly => assembly, _ => scope)
            };
            S_MODULES.Add(handle, module);
            try
            {
                RebuildLocked();
                return handle;
            }
            catch
            {
                S_MODULES.Remove(handle);
                throw;
            }
        }
    }

    /// <summary>
    /// Stages and validates a replacement generation without publishing it.
    /// </summary>
    public static AssemblyReloadSession BeginReload(
        AssemblyModuleHandle module,
        AssemblyLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            if (!S_MODULES.TryGetValue(module, out AssemblyModuleEntry? previous))
                throw new ArgumentException("The assembly module is not active.", nameof(module));
            if (!string.Equals(previous.moduleName, request.moduleName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Reload request module name '{request.moduleName}' does not match '{previous.moduleName}'.",
                    nameof(request));
            }

            return BeginReloadLocked(
                [request],
                removedModuleNames: [],
                new Dictionary<string, AssemblyModuleHandle>(
                    StringComparer.Ordinal) { [request.moduleName] = module });
        }
    }

    /// <summary>
    /// Stages a dependency-ordered set of module additions or replacements as one atomic transaction.
    /// Existing modules are matched by their stable module names; an unknown name creates a new module.
    /// </summary>
    /// <param name="requests">The complete reverse-dependency reload closure.</param>
    /// <returns>A session that publishes or rolls back every candidate together.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requests"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the request set is empty or contains duplicate module names.</exception>
    public static AssemblyReloadSession BeginReload(IReadOnlyList<AssemblyLoadRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            return BeginReloadLocked(requests, removedModuleNames: [], forcedHandles: null);
        }
    }

    /// <summary>
    /// Stages additions, replacements, and removals as one atomic dependency-ordered transaction.
    /// </summary>
    /// <param name="requests">Complete candidate module additions and replacements.</param>
    /// <param name="removedModuleNames">Active stable module names omitted from the candidate generation.</param>
    /// <returns>A session that publishes or rolls back every candidate and removal together.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when no change is requested, a name is duplicated, or one module is both replaced and removed.
    /// </exception>
    public static AssemblyReloadSession BeginReload(
        IReadOnlyList<AssemblyLoadRequest> requests,
        IReadOnlyList<string> removedModuleNames)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(removedModuleNames);
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            return BeginReloadLocked(requests, removedModuleNames, forcedHandles: null);
        }
    }

    /// <summary>
    /// Removes an active module and starts cooperative unload when it is manager-owned.
    /// </summary>
    public static AssemblyUnloadMonitor Unload(AssemblyModuleHandle module)
    {
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            ValidateUnloadClosure([module]);
            if (!S_MODULES.Remove(module, out AssemblyModuleEntry? removed))
                throw new ArgumentException("The assembly module is not active.", nameof(module));
            try
            {
                RebuildLocked();
            }
            catch
            {
                S_MODULES.Add(module, removed);
                throw;
            }

            return BeginUnload(removed);
        }
    }

    /// <summary>
    /// Removes several active modules in one catalog transaction, then requests unload in reverse dependency order.
    /// </summary>
    /// <param name="modules">The distinct active module handles to remove.</param>
    /// <returns>A monitor that completes after every collectible context becomes unreachable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="modules"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when a handle is duplicated or is not active.</exception>
    public static AssemblyUnloadMonitor Unload(IReadOnlyList<AssemblyModuleHandle> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            if (modules.Distinct().Count() != modules.Count)
                throw new ArgumentException("Module handles must be distinct.", nameof(modules));
            ValidateUnloadClosure(modules);
            var removed = new List<AssemblyModuleEntry>(modules.Count);
            foreach (AssemblyModuleHandle handle in modules)
            {
                if (!S_MODULES.TryGetValue(handle, out AssemblyModuleEntry? module))
                    throw new ArgumentException("An assembly module is not active.", nameof(modules));
                removed.Add(module);
            }
            foreach (AssemblyModuleEntry module in removed)
                S_MODULES.Remove(module.handle);
            try
            {
                RebuildLocked();
            }
            catch
            {
                foreach (AssemblyModuleEntry module in removed)
                    S_MODULES.Add(module.handle, module);
                throw;
            }

            AssemblyModuleEntry[] unloadOrder = removed
                .OrderByDescending(static module => module.domain == AssemblyDomain.InnoPlugin
                    ? 0
                    : module.scope == AssemblyScope.Runtime ? 1 : 2)
                .ToArray();
            var monitors = unloadOrder.Select(BeginUnload).ToArray();
            return monitors.Length == 1
                ? monitors[0]
                : new AssemblyUnloadMonitor(monitors);
        }
    }

    /// <summary>
    /// Applies pending host assembly changes without rebuilding an unchanged catalog.
    /// </summary>
    public static void Refresh()
    {
        lock (S_SYNC)
        {
            EnsureInitialized();
            if (s_hostCatalogDirty && !s_reloadInProgress)
                RebuildLocked();
        }
    }

    /// <summary>
    /// Rebuilds the active assembly catalog and every registered derived-state participant.
    /// </summary>
    public static void Rebuild()
    {
        lock (S_SYNC)
        {
            EnsureInitialized();
            EnsureNoReloadInProgress();
            RebuildLocked();
        }
    }

    /// <summary>
    /// Unsubscribes assembly discovery and begins unload of all manager-owned modules.
    /// </summary>
    public static void Shutdown()
    {
        lock (S_SYNC)
            ShutdownLocked();
    }

    internal static void Activate(ReloadState state)
    {
        lock (S_SYNC)
        {
            EnsureActiveState(state);
            if (state.activated)
                return;

            foreach (AssemblyModuleEntry removed in state.removedModules)
                S_MODULES.Remove(removed.handle);
            for (int i = 0; i < state.candidateModules.Length; i++)
                S_MODULES[state.candidateModules[i].handle] = state.candidateModules[i];
            s_currentCatalog = state.candidateCatalog;
            try
            {
                state.refresh.Activate();
                state.activated = true;
            }
            catch
            {
                RestorePreviousModules(state);
                s_currentCatalog = state.previousCatalog;
                state.refresh.Rollback();
                state.finished = true;
                s_reloadInProgress = false;
                BeginUnloadReverse(state.candidateModules);
                throw;
            }
        }
    }

    internal static AssemblyUnloadMonitor Complete(ReloadState state)
    {
        lock (S_SYNC)
        {
            EnsureActiveState(state);
            if (!state.activated)
                throw new InvalidOperationException("The reload session must be activated before completion.");

            state.refresh.Complete();
            state.finished = true;
            s_reloadInProgress = false;
            AssemblyUnloadMonitor monitor = BeginUnloadReverse(
                state.previousModules
                    .OfType<AssemblyModuleEntry>()
                    .Concat(state.removedModules)
                    .Distinct()
                    .ToArray());
            return monitor;
        }
    }

    internal static void Rollback(ReloadState state)
    {
        lock (S_SYNC)
        {
            if (state.finished)
                return;
            if (state.activated)
            {
                RestorePreviousModules(state);
                s_currentCatalog = state.previousCatalog;
            }

            state.refresh.Rollback();
            state.finished = true;
            s_reloadInProgress = false;
            BeginUnloadReverse(state.candidateModules);
        }
    }

    private static AssemblyReloadSession BeginReloadLocked(
        IReadOnlyList<AssemblyLoadRequest> requests,
        IReadOnlyList<string> removedModuleNames,
        IReadOnlyDictionary<string, AssemblyModuleHandle>? forcedHandles)
    {
        if (requests.Count == 0 && removedModuleNames.Count == 0)
            throw new ArgumentException("At least one module change is required.", nameof(requests));
        if (requests.Any(static request => request is null))
            throw new ArgumentException("Module reload requests cannot contain null entries.", nameof(requests));
        AssemblyLoadRequest[] orderedRequests = OrderReloadRequests(requests);
        if (orderedRequests.Select(static request => request.moduleName).Distinct(StringComparer.Ordinal).Count() !=
            orderedRequests.Length)
        {
            throw new ArgumentException("A reload plan cannot contain duplicate module names.", nameof(requests));
        }
        if (removedModuleNames.Any(static name => string.IsNullOrWhiteSpace(name)) ||
            removedModuleNames.Distinct(StringComparer.Ordinal).Count() != removedModuleNames.Count)
        {
            throw new ArgumentException("Removed module names must be non-empty and distinct.", nameof(removedModuleNames));
        }
        if (orderedRequests.Any(request => removedModuleNames.Contains(request.moduleName, StringComparer.Ordinal)))
            throw new ArgumentException("A module cannot be replaced and removed together.", nameof(removedModuleNames));
        AssemblyModuleEntry[] removedModules = removedModuleNames.Select(name =>
        {
            AssemblyModuleEntry? module = S_MODULES.Values.SingleOrDefault(candidate =>
                string.Equals(candidate.moduleName, name, StringComparison.Ordinal));
            return module ?? throw new ArgumentException(
                $"Removed assembly module '{name}' is not active.",
                nameof(removedModuleNames));
        }).ToArray();
        ValidateReloadClosure(orderedRequests, removedModules);
        IReadOnlyDictionary<string, PlannedAssembly> plannedAssemblies =
            BuildPlannedAssemblyMap(orderedRequests);

        var previousModules = new AssemblyModuleEntry?[orderedRequests.Length];
        var candidates = new List<AssemblyModuleEntry>(orderedRequests.Length);
        try
        {
            for (int i = 0; i < orderedRequests.Length; i++)
            {
                AssemblyLoadRequest request = orderedRequests[i];
                AssemblyModuleEntry? previous = FindPreviousModule(request, forcedHandles);
                previousModules[i] = previous;
                AssemblyModuleHandle handle = previous?.handle ?? new AssemblyModuleHandle(Guid.NewGuid());
                int generation = (previous?.generation ?? 0) + 1;
                IReadOnlyList<AssemblyModuleEntry> upstream = GetUpstreamModules(request, candidates);
                candidates.Add(StageModule(handle, request, generation, upstream, plannedAssemblies));
            }

            var replacements = candidates.ToDictionary(static module => module.handle);
            AssemblyCatalogSnapshot previousCatalog = s_currentCatalog;
            AssemblyCatalogSnapshot candidateCatalog = BuildCatalog(
                replacements,
                removedModules.Select(static module => module.handle).ToHashSet());
            AssemblyCatalogRefreshSet refresh = AssemblyCatalogCoordinator.Prepare(candidateCatalog);
            s_reloadInProgress = true;
            return new AssemblyReloadSession(new ReloadState
            {
                previousModules = previousModules,
                removedModules = removedModules,
                candidateModules = candidates.ToArray(),
                previousCatalog = previousCatalog,
                candidateCatalog = candidateCatalog,
                refresh = refresh
            });
        }
        catch
        {
            BeginUnloadReverse(candidates);
            throw;
        }
    }

    private static AssemblyModuleEntry? FindPreviousModule(
        AssemblyLoadRequest request,
        IReadOnlyDictionary<string, AssemblyModuleHandle>? forcedHandles)
    {
        if (forcedHandles is not null && forcedHandles.TryGetValue(request.moduleName, out AssemblyModuleHandle handle))
            return S_MODULES[handle];
        AssemblyModuleEntry? previous = S_MODULES.Values.SingleOrDefault(module =>
            string.Equals(module.moduleName, request.moduleName, StringComparison.Ordinal));
        if (previous is null && request.domain != AssemblyDomain.InnoPlugin)
            ValidateUniqueReloadBoundary(request.domain, request.scope);
        else if (previous is not null &&
                 (previous.domain != request.domain || previous.scope != request.scope))
            throw new InvalidOperationException(
                $"Module '{request.moduleName}' cannot change its domain or scope across generations.");
        return previous;
    }

    private static IReadOnlyList<AssemblyModuleEntry> GetUpstreamModules(
        AssemblyLoadRequest request,
        IReadOnlyList<AssemblyModuleEntry> stagedCandidates)
    {
        IEnumerable<AssemblyModuleEntry> effectiveModules = S_MODULES.Values
            .Where(active => stagedCandidates.All(candidate => candidate.handle != active.handle))
            .Concat(stagedCandidates);
        if (request.upstreamModuleNames.Count != 0)
        {
            Dictionary<string, AssemblyModuleEntry> byName = effectiveModules.ToDictionary(
                static module => module.moduleName,
                StringComparer.Ordinal);
            return request.upstreamModuleNames.Select(name =>
            {
                if (!byName.TryGetValue(name, out AssemblyModuleEntry? module))
                {
                    throw new InvalidOperationException(
                        $"Module '{request.moduleName}' requires unavailable upstream module '{name}'.");
                }
                return module;
            }).ToArray();
        }
        if (request.domain == AssemblyDomain.InnoPlugin)
            return [];
        return request.scope == AssemblyScope.Runtime
            ? effectiveModules.Where(static module => module.domain == AssemblyDomain.InnoPlugin).ToArray()
            : effectiveModules.Where(static module =>
                    module.domain == AssemblyDomain.InnoPlugin ||
                    module.domain == AssemblyDomain.InnoScripting && module.scope == AssemblyScope.Runtime)
                .ToArray();
    }

    private static int GetReloadOrder(AssemblyLoadRequest request)
        => request.domain switch
        {
            AssemblyDomain.InnoPlugin => 0,
            AssemblyDomain.InnoScripting when request.scope == AssemblyScope.Runtime => 1,
            AssemblyDomain.InnoScripting => 2,
            _ => throw new ArgumentException("InnoInternal assemblies cannot be loaded into a collectible module.")
        };

    private static AssemblyLoadRequest[] OrderReloadRequests(
        IReadOnlyList<AssemblyLoadRequest> requests)
    {
        Dictionary<string, AssemblyLoadRequest> byName = requests.ToDictionary(
            static request => request.moduleName,
            StringComparer.Ordinal);
        IComparer<string> ordering = Comparer<string>.Create((left, right) =>
        {
            int domainOrder = GetReloadOrder(byName[left]).CompareTo(GetReloadOrder(byName[right]));
            return domainOrder != 0
                ? domainOrder
                : StringComparer.Ordinal.Compare(left, right);
        });
        var graph = new DependencyGraph<string>(StringComparer.Ordinal, ordering);
        foreach (AssemblyLoadRequest request in requests)
        {
            graph.AddNode(request.moduleName);
            foreach (string dependencyName in request.upstreamModuleNames
                         .OrderBy(static value => value, StringComparer.Ordinal))
            {
                if (!byName.TryGetValue(dependencyName, out AssemblyLoadRequest? dependency))
                    continue;
                if (GetReloadOrder(dependency) > GetReloadOrder(request))
                {
                    throw new ArgumentException(
                        $"Module '{request.moduleName}' cannot depend on downstream module '{dependencyName}'.",
                        nameof(requests));
                }
                graph.AddDependency(request.moduleName, dependencyName);
            }
        }
        try
        {
            return graph.TopologicalSort().Select(name => byName[name]).ToArray();
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException(exception.Message, nameof(requests), exception);
        }
    }

    private static void RestorePreviousModules(ReloadState state)
    {
        for (int i = 0; i < state.candidateModules.Length; i++)
        {
            AssemblyModuleEntry candidate = state.candidateModules[i];
            AssemblyModuleEntry? previous = state.previousModules[i];
            if (previous is null)
                S_MODULES.Remove(candidate.handle);
            else
                S_MODULES[candidate.handle] = previous;
        }
        foreach (AssemblyModuleEntry removed in state.removedModules)
            S_MODULES[removed.handle] = removed;
        s_currentCatalog = state.previousCatalog;
    }

    private static void RebuildLocked()
    {
        if (s_catalogTransitionInProgress)
        {
            s_hostCatalogDirty = true;
            s_rebuildPending = true;
            return;
        }

        do
        {
            s_rebuildPending = false;
            s_catalogTransitionInProgress = true;
            try
            {
                RebuildOnceLocked();
            }
            finally
            {
                s_catalogTransitionInProgress = false;
            }
        }
        while (s_rebuildPending);
    }

    private static void RebuildOnceLocked()
    {
        AssemblyCatalogSnapshot previous = s_currentCatalog;
        s_hostCatalogDirty = false;
        try
        {
            AssemblyCatalogSnapshot candidate = BuildCatalog(
                new Dictionary<AssemblyModuleHandle, AssemblyModuleEntry>());
            AssemblyCatalogRefreshSet refresh = AssemblyCatalogCoordinator.Prepare(candidate);
            s_currentCatalog = candidate;
            try
            {
                refresh.Activate();
                refresh.Complete();
            }
            catch
            {
                s_currentCatalog = previous;
                refresh.Rollback();
                throw;
            }
        }
        catch
        {
            s_hostCatalogDirty = true;
            throw;
        }
    }

    private static AssemblyCatalogSnapshot BuildCatalog(
        IReadOnlyDictionary<AssemblyModuleHandle, AssemblyModuleEntry> replacements,
        IReadOnlySet<AssemblyModuleHandle>? removed = null)
    {
        Assembly[] assemblies = GetActiveAssemblies(replacements, removed ?? new HashSet<AssemblyModuleHandle>());
        return new AssemblyCatalogSnapshot(++s_catalogVersion, assemblies);
    }

    private static Assembly[] GetActiveAssemblies(
        IReadOnlyDictionary<AssemblyModuleHandle, AssemblyModuleEntry> replacements,
        IReadOnlySet<AssemblyModuleHandle> removed)
    {
        IEnumerable<Assembly> host = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .Where(static assembly => AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .Where(IsDiscoverableHostAssembly);
        IEnumerable<AssemblyModuleEntry> modules = S_MODULES.Values
            .Where(module => !removed.Contains(module.handle))
            .Select(module => replacements.GetValueOrDefault(module.handle, module))
            .Concat(replacements.Values.Where(candidate => !S_MODULES.ContainsKey(candidate.handle)));
        IEnumerable<Assembly> moduleAssemblies = modules.SelectMany(static module => module.assemblies);
        return host.Concat(moduleAssemblies).Distinct().ToArray();
    }

    private static AssemblyModuleEntry StageModule(
        AssemblyModuleHandle handle,
        AssemblyLoadRequest request,
        int generation,
        IReadOnlyList<AssemblyModuleEntry> upstreamModules,
        IReadOnlyDictionary<string, PlannedAssembly> plannedAssemblies)
    {
        CleanupRetiredShadowDirectories();
        ValidateRequest(request);
        string generationDirectory = Path.Combine(
            s_options.cacheDirectory,
            SanitizePathSegment(request.moduleName),
            generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(generationDirectory);

        string[] sourcePaths = new[] { request.mainAssemblyPath }
            .Concat(request.preloadAssemblyPaths)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var shadowPathsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var explicitShadowPaths = new List<string>(sourcePaths.Length);
        foreach (string sourcePath in sourcePaths)
        {
            string assemblyName = AssemblyName.GetAssemblyName(sourcePath).Name
                ?? throw new InvalidOperationException($"Assembly '{sourcePath}' has no simple name.");
            if (shadowPathsByName.ContainsKey(assemblyName))
                throw new InvalidOperationException($"Module contains duplicate assembly name '{assemblyName}'.");
            string shadowPath = CopyAssemblyArtifacts(sourcePath, generationDirectory, assemblyName);
            shadowPathsByName.Add(assemblyName, shadowPath);
            explicitShadowPaths.Add(shadowPath);
        }

        string mainSourcePath = Path.GetFullPath(request.mainAssemblyPath);
        string mainShadowPath = explicitShadowPaths[sourcePaths
            .Select((path, index) => (path, index))
            .First(pair => string.Equals(pair.path, mainSourcePath, StringComparison.OrdinalIgnoreCase)).index];
        IReadOnlyDictionary<string, Assembly> sharedAssemblies = BuildSharedAssemblies(
            request,
            upstreamModules,
            shadowPathsByName.Keys);
        var loadContext = new ModuleLoadContext(
            $"{request.moduleName}#{generation}",
            mainShadowPath,
            request.collectible,
            sharedAssemblies,
            shadowPathsByName.Values);

        try
        {
            var assemblies = new List<Assembly>(explicitShadowPaths.Count)
            {
                loadContext.LoadFromAssemblyPath(mainShadowPath)
            };
            foreach (string shadowPath in explicitShadowPaths)
            {
                string name = AssemblyName.GetAssemblyName(shadowPath).Name ?? string.Empty;
                if (assemblies.Any(assembly => string.Equals(
                        assembly.GetName().Name,
                        name,
                        StringComparison.OrdinalIgnoreCase)))
                    continue;
                assemblies.Add(loadContext.LoadFromAssemblyPath(shadowPath));
            }

            Assembly[] loadedAssemblies = assemblies.Distinct().ToArray();
            var loadedScopes = new Dictionary<Assembly, AssemblyScope>(ReferenceEqualityComparer.Instance);
            foreach (Assembly assembly in loadedAssemblies)
            {
                string simpleName = assembly.GetName().Name ?? string.Empty;
                AssemblyScope assemblyScope = request.assemblyScopes.GetValueOrDefault(simpleName, request.scope);
                if (request.domain == AssemblyDomain.InnoScripting &&
                    (!assembly.TryGetInnoAssemblyClassification(
                         out AssemblyDomain declaredDomain,
                         out AssemblyScope declaredScope) ||
                     declaredDomain != AssemblyDomain.InnoScripting ||
                     declaredScope != assemblyScope))
                {
                    throw new InvalidDataException(
                        $"Script assembly '{simpleName}' does not declare its requested domain and scope metadata.");
                }
                assembly.RegisterInnoAssemblyClassification(request.domain, assemblyScope);
                loadedScopes.Add(assembly, assemblyScope);
            }
            ValidateLoadedModule(
                request,
                loadedAssemblies,
                loadedScopes,
                sharedAssemblies,
                upstreamModules,
                plannedAssemblies);

            return new AssemblyModuleEntry
            {
                handle = handle,
                moduleName = request.moduleName,
                generation = generation,
                externallyOwned = false,
                collectible = request.collectible,
                domain = request.domain,
                scope = request.scope,
                assemblies = loadedAssemblies,
                assemblyScopes = loadedScopes,
                upstreamModuleNames = request.upstreamModuleNames.ToArray(),
                loadContext = loadContext,
                shadowDirectory = generationDirectory
            };
        }
        catch
        {
            if (request.collectible)
            {
                var reference = new WeakReference(loadContext, trackResurrection: false);
                loadContext.Unload();
                S_PENDING_UNLOADS.Add(new AssemblyUnloadMonitor(reference, generationDirectory));
                CleanupRetiredShadowDirectories();
            }
            throw;
        }
    }

    private static IReadOnlyDictionary<string, Assembly> BuildSharedAssemblies(
        AssemblyLoadRequest request,
        IReadOnlyList<AssemblyModuleEntry> upstreamModules,
        IEnumerable<string> ownedNames)
    {
        var result = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .Where(static assembly => AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .GroupBy(static assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var owned = new HashSet<string>(ownedNames, StringComparer.OrdinalIgnoreCase);
        foreach (string ownedName in owned)
        {
            if (result.ContainsKey(ownedName))
            {
                throw new InvalidDataException(
                    $"Module assembly '{ownedName}' duplicates an assembly already loaded in the default context.");
            }
        }

        foreach (AssemblyModuleEntry upstream in upstreamModules)
        {
            foreach (Assembly assembly in upstream.assemblies)
            {
                if (request.scope == AssemblyScope.Runtime &&
                    upstream.assemblyScopes[assembly] == AssemblyScope.Editor)
                {
                    continue;
                }
                string name = assembly.GetName().Name ?? string.Empty;
                if (owned.Contains(name) || !result.TryAdd(name, assembly))
                    throw new InvalidDataException($"Reload graph contains duplicate managed assembly name '{name}'.");
            }
        }
        return result;
    }

    private static void ValidateLoadedModule(
        AssemblyLoadRequest request,
        IReadOnlyList<Assembly> assemblies,
        IReadOnlyDictionary<Assembly, AssemblyScope> assemblyScopes,
        IReadOnlyDictionary<string, Assembly> sharedAssemblies,
        IReadOnlyList<AssemblyModuleEntry> upstreamModules,
        IReadOnlyDictionary<string, PlannedAssembly> plannedAssemblies)
    {
        var ownByName = assemblies.ToDictionary(
            static assembly => assembly.GetName().Name ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
        ValidateOwnedDependencyGraph(ownByName);
        var forbiddenDownstreamNames = S_MODULES.Values
            .Where(module => request.domain == AssemblyDomain.InnoPlugin
                ? module.domain == AssemblyDomain.InnoScripting
                : request.scope == AssemblyScope.Runtime && module.scope == AssemblyScope.Editor)
            .SelectMany(static module => module.assemblies)
            .Select(static assembly => assembly.GetName().Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var upstreamByName = upstreamModules
            .SelectMany(module => module.assemblies.Select(assembly => (module, assembly)))
            .ToDictionary(
                static pair => pair.assembly.GetName().Name ?? string.Empty,
                static pair => pair,
                StringComparer.OrdinalIgnoreCase);

        foreach (Assembly assembly in assemblies)
        {
            AssemblyScope sourceScope = assemblyScopes[assembly];
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name ?? string.Empty;
                if (ownByName.TryGetValue(name, out Assembly? ownedDependency))
                {
                    if (sourceScope == AssemblyScope.Runtime &&
                        assemblyScopes[ownedDependency] == AssemblyScope.Editor)
                    {
                        throw new InvalidDataException(
                            $"Runtime assembly '{assembly.GetName().Name}' cannot reference editor assembly '{name}'.");
                    }
                    continue;
                }
                if (forbiddenDownstreamNames.Contains(name))
                {
                    throw new InvalidDataException(
                        $"Assembly '{assembly.GetName().Name}' has a forbidden downstream reference to '{name}'.");
                }
                if (upstreamByName.TryGetValue(name, out var upstream))
                {
                    if (sourceScope == AssemblyScope.Runtime &&
                        upstream.module.assemblyScopes[upstream.assembly] == AssemblyScope.Editor)
                    {
                        throw new InvalidDataException(
                            $"Runtime assembly '{assembly.GetName().Name}' cannot reference editor assembly '{name}'.");
                    }
                    continue;
                }
                if (plannedAssemblies.TryGetValue(name, out PlannedAssembly planned))
                {
                    if (request.domain == AssemblyDomain.InnoPlugin &&
                        planned.domain == AssemblyDomain.InnoScripting)
                    {
                        throw new InvalidDataException(
                            $"Plugin assembly '{assembly.GetName().Name}' cannot reference project script assembly '{name}'.");
                    }
                    if (sourceScope == AssemblyScope.Runtime && planned.scope == AssemblyScope.Editor)
                    {
                        throw new InvalidDataException(
                            $"Runtime assembly '{assembly.GetName().Name}' cannot reference editor assembly '{name}'.");
                    }
                    throw new InvalidDataException(
                        $"Assembly '{assembly.GetName().Name}' has an unavailable downstream reference to '{name}'.");
                }
                if (S_TRUSTED_PLATFORM_ASSEMBLIES.Contains(name))
                    continue;
                if (sharedAssemblies.TryGetValue(name, out Assembly? sharedAssembly))
                {
                    if (!sharedAssembly.TryGetInnoAssemblyClassification(
                            out AssemblyDomain sharedDomain,
                            out AssemblyScope sharedScope) ||
                        sharedDomain != AssemblyDomain.InnoInternal)
                    {
                        throw new InvalidDataException(
                            $"Assembly '{assembly.GetName().Name}' can only share BCL or InnoInternal contracts, not '{name}'.");
                    }
                    if (sourceScope == AssemblyScope.Runtime && sharedScope == AssemblyScope.Editor)
                    {
                        throw new InvalidDataException(
                            $"Runtime assembly '{assembly.GetName().Name}' cannot reference editor contract '{name}'.");
                    }
                    continue;
                }
                throw new InvalidDataException(
                    $"Assembly '{assembly.GetName().Name}' references unavailable assembly '{name}'. " +
                    "Include the dependency in its module or load an InnoInternal contract in the host.");
            }
        }
    }

    private static void ValidateOwnedDependencyGraph(IReadOnlyDictionary<string, Assembly> assemblies)
    {
        var graph = new DependencyGraph<string>(
            StringComparer.OrdinalIgnoreCase,
            StringComparer.Ordinal);
        foreach (string name in assemblies.Keys)
            graph.AddNode(name);
        foreach ((string name, Assembly assembly) in assemblies)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is string dependency && assemblies.ContainsKey(dependency))
                    graph.AddDependency(name, dependency);
            }
        }
        if (graph.TryFindCycle(out IReadOnlyList<string> cycle))
        {
            throw new InvalidDataException(
                $"Module assembly reference cycle: {string.Join(" -> ", cycle)}.");
        }
    }

    private static string CopyAssemblyArtifacts(
        string sourcePath,
        string destinationDirectory,
        string assemblyName)
    {
        string destinationPath = Path.Combine(destinationDirectory, assemblyName + ".dll");
        File.Copy(sourcePath, destinationPath, overwrite: true);
        string sourcePdb = Path.ChangeExtension(sourcePath, ".pdb");
        if (File.Exists(sourcePdb))
            File.Copy(sourcePdb, Path.Combine(destinationDirectory, Path.GetFileName(sourcePdb)), overwrite: true);
        string sourceDeps = Path.ChangeExtension(sourcePath, ".deps.json");
        if (File.Exists(sourceDeps))
            File.Copy(sourceDeps, Path.Combine(destinationDirectory, Path.GetFileName(sourceDeps)), overwrite: true);
        return destinationPath;
    }

    private static AssemblyUnloadMonitor BeginUnload(AssemblyModuleEntry module)
    {
        if (module.externallyOwned || module.loadContext is null || !module.collectible)
            return new AssemblyUnloadMonitor(loadContext: null);

        var reference = new WeakReference(module.loadContext, trackResurrection: false);
        module.loadContext.Unload();
        var monitor = new AssemblyUnloadMonitor(reference, module.shadowDirectory);
        S_PENDING_UNLOADS.Add(monitor);
        CleanupRetiredShadowDirectories();
        return monitor;
    }

    private static AssemblyUnloadMonitor BeginUnloadReverse(IReadOnlyList<AssemblyModuleEntry> modules)
    {
        var monitors = new List<AssemblyUnloadMonitor>(modules.Count);
        for (int i = modules.Count - 1; i >= 0; i--)
            monitors.Add(BeginUnload(modules[i]));
        return monitors.Count == 1
            ? monitors[0]
            : new AssemblyUnloadMonitor(monitors);
    }

    private static void CleanupRetiredShadowDirectories()
    {
        for (int i = S_PENDING_UNLOADS.Count - 1; i >= 0; i--)
        {
            if (S_PENDING_UNLOADS[i].TryCleanupShadowDirectory())
                S_PENDING_UNLOADS.RemoveAt(i);
        }
    }

    private static void CleanupStaleShadowDirectories()
    {
        foreach (string directory in Directory.EnumerateDirectories(s_options.cacheDirectory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A still-reachable load context can keep its shadow files locked until a later refresh.
            }
            catch (UnauthorizedAccessException)
            {
                // A later manager initialization can retry cleanup after external file handles are released.
            }
        }
    }

    private static void ValidateRequest(AssemblyLoadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.moduleName))
            throw new ArgumentException("Module name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.mainAssemblyPath))
            throw new ArgumentException("Main assembly path is required.", nameof(request));
        if (!request.collectible)
            throw new ArgumentException("Plugin and scripting modules must use a collectible load context.", nameof(request));
        if (!Enum.IsDefined(request.domain) || request.domain == AssemblyDomain.InnoInternal)
            throw new ArgumentException("A reloadable module must belong to InnoPlugin or InnoScripting.", nameof(request));
        if (!Enum.IsDefined(request.scope) || request.assemblyScopes.Values.Any(static scope => !Enum.IsDefined(scope)))
            throw new ArgumentException("A reloadable module contains an invalid assembly scope.", nameof(request));
        if (request.upstreamModuleNames.Any(static name => string.IsNullOrWhiteSpace(name)) ||
            request.upstreamModuleNames.Distinct(StringComparer.Ordinal).Count() !=
            request.upstreamModuleNames.Count ||
            request.upstreamModuleNames.Contains(request.moduleName, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Upstream module names must be non-empty, distinct, and cannot reference the owning module.",
                nameof(request));
        }

        foreach (string path in new[] { request.mainAssemblyPath }.Concat(request.preloadAssemblyPaths))
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("A module assembly does not exist.", path);
            try
            {
                _ = AssemblyName.GetAssemblyName(path);
            }
            catch (BadImageFormatException exception)
            {
                throw new ArgumentException($"Module file '{path}' is not a managed assembly.", nameof(request), exception);
            }
        }
    }

    private static IReadOnlyDictionary<string, PlannedAssembly> BuildPlannedAssemblyMap(
        IReadOnlyList<AssemblyLoadRequest> requests)
    {
        var result = new Dictionary<string, PlannedAssembly>(StringComparer.OrdinalIgnoreCase);
        foreach (AssemblyLoadRequest request in requests)
        {
            ValidateRequest(request);
            var ownedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in new[] { request.mainAssemblyPath }
                         .Concat(request.preloadAssemblyPaths)
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string name = AssemblyName.GetAssemblyName(path).Name
                    ?? throw new InvalidOperationException($"Assembly '{path}' has no simple name.");
                ownedNames.Add(name);
                AssemblyScope scope = request.assemblyScopes.GetValueOrDefault(name, request.scope);
                if (!result.TryAdd(name, new PlannedAssembly(request.domain, scope)))
                    throw new InvalidDataException($"Reload plan contains duplicate managed assembly name '{name}'.");
            }
            string? unknownScope = request.assemblyScopes.Keys.FirstOrDefault(name => !ownedNames.Contains(name));
            if (unknownScope is not null)
            {
                throw new InvalidDataException(
                    $"Module '{request.moduleName}' declares a scope for unknown assembly '{unknownScope}'.");
            }
        }
        return result;
    }

    private static HashSet<string> GetTrustedPlatformAssemblyNames()
    {
        string paths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateUniqueModuleName(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name is required.", nameof(moduleName));
        if (S_MODULES.Values.Any(module => string.Equals(module.moduleName, moduleName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Assembly module '{moduleName}' is already active.");
    }

    private static void ValidateUniqueReloadBoundary(AssemblyDomain domain, AssemblyScope scope)
    {
        if (domain == AssemblyDomain.InnoPlugin)
            return;
        if (S_MODULES.Values.Any(module => module.domain == domain && module.scope == scope))
        {
            throw new InvalidOperationException(
                $"Only one {scope} scripting module can be active.");
        }
    }

    private static void ValidateReloadClosure(
        IReadOnlyList<AssemblyLoadRequest> requests,
        IReadOnlyList<AssemblyModuleEntry> removedModules)
    {
        bool reloadsPlugins = requests.Any(static request => request.domain == AssemblyDomain.InnoPlugin) ||
                              removedModules.Any(static module => module.domain == AssemblyDomain.InnoPlugin);
        bool reloadsRuntime = requests.Any(static request =>
            request.domain == AssemblyDomain.InnoScripting && request.scope == AssemblyScope.Runtime);
        bool reloadsEditor = requests.Any(static request =>
            request.domain == AssemblyDomain.InnoScripting && request.scope == AssemblyScope.Editor);
        if (reloadsPlugins && S_MODULES.Values.Any(static module => module.domain == AssemblyDomain.InnoScripting) &&
            (!reloadsRuntime || !reloadsEditor))
        {
            throw new InvalidOperationException(
                "Reloading plugins requires both dependent scripting modules in the same transaction.");
        }
        if (reloadsRuntime && S_MODULES.Values.Any(static module =>
                module.domain == AssemblyDomain.InnoScripting && module.scope == AssemblyScope.Editor) &&
            !reloadsEditor)
        {
            throw new InvalidOperationException(
                "Reloading runtime scripts requires the editor scripting module in the same transaction.");
        }
        HashSet<string> reloadedNames = requests
            .Select(static request => request.moduleName)
            .ToHashSet(StringComparer.Ordinal);
        reloadedNames.UnionWith(removedModules.Select(static module => module.moduleName));
        HashSet<string> reloadedPlugins = requests
            .Where(static request => request.domain == AssemblyDomain.InnoPlugin)
            .Select(static request => request.moduleName)
            .ToHashSet(StringComparer.Ordinal);
        reloadedPlugins.UnionWith(removedModules
            .Where(static module => module.domain == AssemblyDomain.InnoPlugin)
            .Select(static module => module.moduleName));
        string? omittedDependent = S_MODULES.Values.FirstOrDefault(module =>
            module.domain == AssemblyDomain.InnoPlugin &&
            !reloadedNames.Contains(module.moduleName) &&
            module.upstreamModuleNames.Any(reloadedPlugins.Contains))?.moduleName;
        if (omittedDependent is not null)
        {
            throw new InvalidOperationException(
                $"Reloading a Plugin module also requires dependent module '{omittedDependent}'.");
        }
    }

    private static void ValidateUnloadClosure(IReadOnlyList<AssemblyModuleHandle> handles)
    {
        var removed = handles.ToHashSet();
        bool removesPlugin = S_MODULES.Values.Any(module =>
            removed.Contains(module.handle) && module.domain == AssemblyDomain.InnoPlugin);
        bool removesRuntime = S_MODULES.Values.Any(module =>
            removed.Contains(module.handle) &&
            module.domain == AssemblyDomain.InnoScripting &&
            module.scope == AssemblyScope.Runtime);
        if (removesPlugin && S_MODULES.Values.Any(module =>
                !removed.Contains(module.handle) && module.domain == AssemblyDomain.InnoScripting))
        {
            throw new InvalidOperationException(
                "Plugin unload requires all dependent scripting modules in the same transaction.");
        }
        HashSet<string> removedNames = S_MODULES.Values
            .Where(module => removed.Contains(module.handle))
            .Select(static module => module.moduleName)
            .ToHashSet(StringComparer.Ordinal);
        if (S_MODULES.Values.Any(module =>
                !removed.Contains(module.handle) &&
                module.upstreamModuleNames.Any(removedNames.Contains)))
        {
            throw new InvalidOperationException(
                "A module cannot unload while an active module depends on it.");
        }
        if (removesRuntime && S_MODULES.Values.Any(module =>
                !removed.Contains(module.handle) &&
                module.domain == AssemblyDomain.InnoScripting &&
                module.scope == AssemblyScope.Editor))
        {
            throw new InvalidOperationException(
                "Runtime scripting unload requires the editor scripting module in the same transaction.");
        }
    }

    private static void EnsureInitialized()
    {
        if (!isInitialized)
            throw new InvalidOperationException("AssemblyManager is not initialized.");
    }

    private static void EnsureNoReloadInProgress()
    {
        if (s_reloadInProgress)
            throw new InvalidOperationException("Another assembly reload transaction is already active.");
    }

    private static void EnsureActiveState(ReloadState state)
    {
        if (state.finished)
            throw new InvalidOperationException("The reload session is already finished.");
        if (!s_reloadInProgress)
            throw new InvalidOperationException("The reload session is no longer current.");
        for (int i = 0; i < state.candidateModules.Length; i++)
        {
            AssemblyModuleEntry candidate = state.candidateModules[i];
            AssemblyModuleEntry? expected = state.activated ? candidate : state.previousModules[i];
            bool exists = S_MODULES.TryGetValue(candidate.handle, out AssemblyModuleEntry? current);
            if (expected is null ? exists : !exists || !ReferenceEquals(current, expected))
                throw new InvalidOperationException("The reload session is no longer current.");
        }
        foreach (AssemblyModuleEntry removed in state.removedModules)
        {
            bool exists = S_MODULES.TryGetValue(removed.handle, out AssemblyModuleEntry? current);
            if (state.activated ? exists : !exists || !ReferenceEquals(current, removed))
                throw new InvalidOperationException("The reload session removal set is no longer current.");
        }
    }

    private static bool IsDiscoverableHostAssembly(Assembly assembly)
    {
        return assembly.TryGetInnoAssemblyClassification(
                   out AssemblyDomain domain,
                   out _) &&
               domain == AssemblyDomain.InnoInternal;
    }

    private static void PreloadInnoHostDependencies()
    {
        Assembly? entry = Assembly.GetEntryAssembly();
        Assembly[] roots = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .Where(static assembly => AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .Where(assembly => ReferenceEquals(assembly, entry) ||
                               (assembly.GetName().Name ?? string.Empty).StartsWith(
                                   "Inno.",
                                   StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        if (roots.Length == 0)
            return;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>();
        foreach (Assembly root in roots)
        {
            string rootName = root.GetName().Name ?? string.Empty;
            if (!string.IsNullOrEmpty(rootName))
                visited.Add(rootName);
            pending.Enqueue(root);
        }
        foreach (AssemblyName dependency in HostDependencyManifest.GetInnoRuntimeAssemblies(roots))
            TryEnqueueHostAssembly(dependency, visited, pending);

        while (pending.Count > 0)
        {
            Assembly assembly = pending.Dequeue();
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                TryEnqueueHostAssembly(reference, visited, pending);
        }
    }

    private static void TryEnqueueHostAssembly(
        AssemblyName assemblyName,
        ISet<string> visited,
        Queue<Assembly> pending)
    {
        string name = assemblyName.Name ?? string.Empty;
        if (!name.StartsWith("Inno.", StringComparison.Ordinal) || !visited.Add(name))
            return;
        try
        {
            pending.Enqueue(Assembly.Load(assemblyName));
        }
        catch (FileNotFoundException)
        {
            // Optional engine modules can be absent from a host deployment.
        }
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private readonly record struct PlannedAssembly(AssemblyDomain domain, AssemblyScope scope);

    private static void OnAssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
    {
        if (AssemblyLoadContext.GetLoadContext(args.LoadedAssembly) == AssemblyLoadContext.Default)
            s_hostCatalogDirty = true;
    }

    private static void ShutdownLocked()
    {
        if (s_assemblyLoadSubscribed)
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
            s_assemblyLoadSubscribed = false;
        }

        var emptyCatalog = new AssemblyCatalogSnapshot(++s_catalogVersion, []);
        AssemblyCatalogRefreshSet refresh = AssemblyCatalogCoordinator.Prepare(emptyCatalog);
        s_currentCatalog = emptyCatalog;
        refresh.Activate();
        refresh.Complete();

        foreach (AssemblyModuleEntry module in S_MODULES.Values
                     .OrderByDescending(static value => value.domain == AssemblyDomain.InnoPlugin
                         ? 0
                         : value.scope == AssemblyScope.Runtime ? 1 : 2))
        {
            BeginUnload(module);
        }
        S_MODULES.Clear();
        isInitialized = false;
        s_hostCatalogDirty = false;
        s_catalogTransitionInProgress = false;
        s_rebuildPending = false;
        s_reloadInProgress = false;
        s_catalogVersion = 0;
        s_currentCatalog = new AssemblyCatalogSnapshot(0, []);
    }
}
