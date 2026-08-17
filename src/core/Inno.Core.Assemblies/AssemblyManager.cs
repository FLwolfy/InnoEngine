using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

using Inno.Core.Assemblies.Internal;
using Inno.Core.Assemblies.Loading;
using Inno.Core.Reflection;

namespace Inno.Core.Assemblies;

/// <summary>
/// Owns the active managed assembly catalog and transactional module generations.
/// </summary>
public static class AssemblyManager
{
    private static readonly object S_SYNC = new();
    private static readonly Dictionary<AssemblyModuleHandle, AssemblyModuleEntry> S_MODULES = [];

    private static AssemblyManagerOptions s_options = new();
    private static long s_typeCacheVersion;
    private static volatile bool s_hostCatalogDirty;
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
    /// Initializes host discovery and publishes the first type-cache snapshot.
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
            if (s_options.preloadEntryAssemblyDependencies)
                PreloadInnoHostDependencies();

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            s_assemblyLoadSubscribed = true;
            isInitialized = true;
            s_hostCatalogDirty = true;
            TypeCacheManager.ConfigureRefreshProvider(EnsureFresh);
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

            AssemblyModuleHandle handle = new(Guid.NewGuid());
            AssemblyModuleEntry module = StageModule(handle, request, generation: 1);
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

            AssemblyModuleHandle handle = new(Guid.NewGuid());
            var module = new AssemblyModuleEntry
            {
                handle = handle,
                moduleName = moduleName,
                generation = 1,
                externallyOwned = true,
                collectible = false,
                assemblies = assemblies.Distinct().ToArray()
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

            AssemblyModuleEntry candidate = StageModule(module, request, previous.generation + 1);
            try
            {
                TypeCacheSnapshot previousTypes = TypeCache.current;
                TypeCacheSnapshot candidateTypes = BuildSnapshot(previousTypes, previous, candidate);
                RegistryRefreshSet registryRefresh = RegistryCoordinator.Prepare(candidateTypes);
                s_reloadInProgress = true;
                return new AssemblyReloadSession(new ReloadState
                {
                    previousModule = previous,
                    candidateModule = candidate,
                    previousTypes = previousTypes,
                    candidateTypes = candidateTypes,
                    registryRefresh = registryRefresh
                });
            }
            catch
            {
                BeginUnload(candidate);
                throw;
            }
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
    /// Rebuilds TypeCache and all active type registries from the current assembly catalog.
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

            S_MODULES[state.previousModule.handle] = state.candidateModule;
            TypeCacheManager.Commit(state.candidateTypes);
            try
            {
                state.registryRefresh.Activate();
                state.activated = true;
            }
            catch
            {
                S_MODULES[state.previousModule.handle] = state.previousModule;
                TypeCacheManager.Commit(state.previousTypes);
                state.registryRefresh.Rollback();
                state.finished = true;
                s_reloadInProgress = false;
                BeginUnload(state.candidateModule);
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

            state.registryRefresh.Complete();
            state.finished = true;
            s_reloadInProgress = false;
            return BeginUnload(state.previousModule);
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
                S_MODULES[state.previousModule.handle] = state.previousModule;
                TypeCacheManager.Commit(state.previousTypes);
            }

            state.registryRefresh.Rollback();
            state.finished = true;
            s_reloadInProgress = false;
            BeginUnload(state.candidateModule);
        }
    }

    private static void EnsureFresh()
    {
        lock (S_SYNC)
        {
            while (isInitialized && s_hostCatalogDirty && !s_reloadInProgress)
                RebuildLocked();
        }
    }

    private static void RebuildLocked()
    {
        TypeCacheSnapshot previous = TypeCacheManager.isInitialized
            ? TypeCacheManager.PeekCurrent()
            : TypeCacheSnapshot.empty;
        s_hostCatalogDirty = false;
        try
        {
            TypeCacheSnapshot candidate = BuildSnapshot(previous, replaced: null, replacement: null);
            RegistryRefreshSet registries = RegistryCoordinator.Prepare(candidate);
            TypeCacheManager.Commit(candidate);
            try
            {
                registries.Activate();
                registries.Complete();
            }
            catch
            {
                TypeCacheManager.Commit(previous);
                registries.Rollback();
                throw;
            }
        }
        catch
        {
            s_hostCatalogDirty = true;
            throw;
        }
    }

    private static TypeCacheSnapshot BuildSnapshot(
        TypeCacheSnapshot previous,
        AssemblyModuleEntry? replaced,
        AssemblyModuleEntry? replacement)
    {
        Assembly[] assemblies = GetActiveAssemblies(replaced, replacement);
        return TypeCacheSnapshot.Build(assemblies, previous, ++s_typeCacheVersion);
    }

    private static Assembly[] GetActiveAssemblies(
        AssemblyModuleEntry? replaced,
        AssemblyModuleEntry? replacement)
    {
        IEnumerable<Assembly> host = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .Where(static assembly => AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .Where(IsDiscoverableHostAssembly);
        IEnumerable<AssemblyModuleEntry> modules = S_MODULES.Values;
        IEnumerable<Assembly> moduleAssemblies = modules.SelectMany(module =>
            ReferenceEquals(module, replaced) && replacement is not null
                ? replacement.assemblies
                : module.assemblies);
        return host.Concat(moduleAssemblies).Distinct().ToArray();
    }

    private static AssemblyModuleEntry StageModule(
        AssemblyModuleHandle handle,
        AssemblyLoadRequest request,
        int generation)
    {
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

        foreach (string directory in sourcePaths
                     .Select(static path => Path.GetDirectoryName(path)!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string dependencyPath in Directory
                         .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                         .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    string dependencyName = AssemblyName.GetAssemblyName(dependencyPath).Name
                        ?? throw new InvalidOperationException($"Assembly '{dependencyPath}' has no simple name.");
                    if (!shadowPathsByName.ContainsKey(dependencyName))
                    {
                        shadowPathsByName.Add(
                            dependencyName,
                            CopyAssemblyArtifacts(dependencyPath, generationDirectory, dependencyName));
                    }
                }
                catch (BadImageFormatException)
                {
                    // Native libraries are not part of the managed dependency catalog.
                }
            }
        }

        string mainSourcePath = Path.GetFullPath(request.mainAssemblyPath);
        string mainShadowPath = explicitShadowPaths[sourcePaths
            .Select((path, index) => (path, index))
            .First(pair => string.Equals(pair.path, mainSourcePath, StringComparison.OrdinalIgnoreCase)).index];
        IReadOnlyDictionary<string, Assembly> sharedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .Where(static assembly => AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            .Where(static assembly => (assembly.GetName().Name ?? string.Empty).StartsWith("Inno.", StringComparison.Ordinal))
            .GroupBy(static assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
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

            return new AssemblyModuleEntry
            {
                handle = handle,
                moduleName = request.moduleName,
                generation = generation,
                externallyOwned = false,
                collectible = request.collectible,
                assemblies = loadContext.Assemblies.ToArray(),
                loadContext = loadContext,
                shadowDirectory = generationDirectory
            };
        }
        catch
        {
            loadContext.Unload();
            throw;
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
        return new AssemblyUnloadMonitor(reference);
    }

    private static void ValidateRequest(AssemblyLoadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.moduleName))
            throw new ArgumentException("Module name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.mainAssemblyPath))
            throw new ArgumentException("Main assembly path is required.", nameof(request));

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

    private static void ValidateUniqueModuleName(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name is required.", nameof(moduleName));
        if (S_MODULES.Values.Any(module => string.Equals(module.moduleName, moduleName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Assembly module '{moduleName}' is already active.");
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
        if (!s_reloadInProgress ||
            !S_MODULES.TryGetValue(state.previousModule.handle, out AssemblyModuleEntry? current) ||
            !ReferenceEquals(current, state.activated ? state.candidateModule : state.previousModule))
            throw new InvalidOperationException("The reload session is no longer current.");
    }

    private static bool IsDiscoverableHostAssembly(Assembly assembly)
    {
        AssemblyGroup group = assembly.GetInnoAssemblyGroup();
        return group is AssemblyGroup.Core or AssemblyGroup.Game or AssemblyGroup.Plugin or AssemblyGroup.Editor;
    }

    private static void PreloadInnoHostDependencies()
    {
        Assembly? entry = Assembly.GetEntryAssembly();
        if (entry is null)
            return;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>();
        pending.Enqueue(entry);
        while (pending.Count > 0)
        {
            Assembly assembly = pending.Dequeue();
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name ?? string.Empty;
                if (!name.StartsWith("Inno.", StringComparison.Ordinal) || !visited.Add(name))
                    continue;
                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (FileNotFoundException)
                {
                    // Optional engine modules can be absent from a host deployment.
                }
            }
        }
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

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

        if (TypeCacheManager.isInitialized)
        {
            RegistryRefreshSet registries = RegistryCoordinator.Prepare(TypeCacheSnapshot.empty);
            TypeCacheManager.Commit(TypeCacheSnapshot.empty);
            registries.Activate();
            registries.Complete();
        }

        foreach (AssemblyModuleEntry module in S_MODULES.Values)
            BeginUnload(module);
        S_MODULES.Clear();
        TypeCacheManager.Shutdown();
        isInitialized = false;
        s_hostCatalogDirty = false;
        s_reloadInProgress = false;
        s_typeCacheVersion = 0;
    }
}
