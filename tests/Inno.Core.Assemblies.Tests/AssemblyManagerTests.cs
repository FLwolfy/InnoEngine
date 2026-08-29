using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Inno.Core.Assemblies;
using Inno.Core.Reflection;

using Xunit;

namespace Inno.Core.Assemblies.Tests;

public sealed class AssemblyManagerTests : IDisposable
{
    private static readonly Guid S_RELOADABLE_TYPE_ID =
        Guid.Parse("44a4cda2-a03e-4918-8db2-f37048a9e4f1");

    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoAssemblyManagerTests",
        Guid.NewGuid().ToString("N"));

    public AssemblyManagerTests()
    {
        AssemblyManager.Initialize(new AssemblyManagerOptions { cacheDirectory = m_cacheDirectory });
        TypeCacheManager.Initialize();
    }

    public void Dispose()
    {
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        ForceCollection();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void LoadAndReloadPublishOnlyTheActiveGeneration()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        TypeRef previousRef = new(S_RELOADABLE_TYPE_ID);
        Type previous = previousRef.Resolve();
        Assert.Equal(1, ReadVersion(previous));
        int previousRuntimeId = TypeCacheManager.GetTypeRef(previous).runtimeId;

        using AssemblyReloadSession reload = AssemblyManager.BeginReload(handle, CreateRequest("V2"));
        TypeCacheReloadContext typeReload = reload.context.GetContext<TypeCacheReloadContext>();
        Assert.True(typeReload.TryResolveReplacement(previousRef, out TypeRef candidateRef));
        Type candidate = candidateRef.Resolve(typeReload.candidate);
        Assert.NotSame(previous, candidate);
        Assert.Same(previous, previousRef.Resolve(typeReload.previous));
        Assert.Equal(previousRef, candidateRef);
        Assert.Equal(previousRef.GetHashCode(), candidateRef.GetHashCode());
        Assert.NotEqual(previousRef.runtimeId, candidateRef.runtimeId);

        reload.Activate();

        Type current = previousRef.Resolve();
        Assert.Same(candidate, current);
        Assert.Equal(2, ReadVersion(current));
        int currentRuntimeId = TypeCacheManager.GetTypeRef(current).runtimeId;
        Assert.NotEqual(previousRuntimeId, currentRuntimeId);
        _ = reload.Complete();
        Assert.Throws<InvalidOperationException>(() => _ = typeReload.previous);
    }

    [Fact]
    public void RollbackRestoresThePreviousTypeAndRegistrySnapshot()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        using var registry = new ReloadableTypeRegistry();
        Type previous = Assert.Single(registry.types);

        using AssemblyReloadSession reload = AssemblyManager.BeginReload(handle, CreateRequest("V2"));
        reload.Activate();
        Assert.NotSame(previous, Assert.Single(registry.types));

        reload.Rollback();

        Assert.Same(previous, Assert.Single(registry.types));
        Assert.Same(previous, new TypeRef(S_RELOADABLE_TYPE_ID).Resolve());
    }

    [Fact]
    public void InvalidCandidateLeavesTheActiveGenerationUntouched()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        Type previous = new TypeRef(S_RELOADABLE_TYPE_ID).Resolve();

        Assert.Throws<InvalidOperationException>(
            () => AssemblyManager.BeginReload(handle, CreateRequest("Invalid")));

        Assert.Same(previous, new TypeRef(S_RELOADABLE_TYPE_ID).Resolve());
        Assert.Equal(1, AssemblyManager.modules.Single().generation);
    }

    [Fact]
    public void RejectedCandidateContextCanBeCollected()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");

        WeakReference candidateContext = CaptureRejectedCandidateContext(handle);
        ForceCollection();

        Assert.False(candidateContext.IsAlive);
        Assert.Equal(1, AssemblyManager.modules.Single().generation);
    }

    [Fact]
    public void RuntimeAssemblyCannotReferenceEditorAssemblyInTheSamePluginModule()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Modules", "V1");
        string dependency = Path.Combine(directory, "Reloadable.PrivateDependency.dll");
        var request = new AssemblyLoadRequest
        {
            moduleName = "InvalidPluginScope",
            mainAssemblyPath = Path.Combine(directory, "Inno.Core.Assemblies.TestModule.dll"),
            preloadAssemblyPaths = [dependency],
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime,
            assemblyScopes = new Dictionary<string, AssemblyScope>(StringComparer.OrdinalIgnoreCase)
            {
                ["Reloadable.PrivateDependency"] = AssemblyScope.Editor
            }
        };

        Assert.Throws<InvalidDataException>(() => AssemblyManager.Load(request));
        Assert.Empty(AssemblyManager.modules);
    }

    [Fact]
    public void ExplicitPluginDependenciesUseSeparateContextsAndRemoveAtomically()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Modules", "V1");
        var dependency = new AssemblyLoadRequest
        {
            moduleName = "Plugin.Dependency",
            mainAssemblyPath = Path.Combine(directory, "Reloadable.PrivateDependency.dll"),
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime
        };
        var consumer = new AssemblyLoadRequest
        {
            moduleName = "Plugin.Consumer",
            mainAssemblyPath = Path.Combine(directory, "Inno.Core.Assemblies.TestModule.dll"),
            upstreamModuleNames = [dependency.moduleName],
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime
        };

        using (AssemblyReloadSession addition = AssemblyManager.BeginReload([consumer, dependency]))
        {
            addition.Activate();
            _ = addition.Complete();
        }

        AssemblyModuleInfo consumerInfo = AssemblyManager.modules.Single(module =>
            module.moduleName == consumer.moduleName);
        Assert.Equal([dependency.moduleName], consumerInfo.upstreamModuleNames);
        Type consumerType = new TypeRef(S_RELOADABLE_TYPE_ID).Resolve();
        Assert.Equal(1, ReadVersion(consumerType));
        Assembly dependencyAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(assembly =>
            string.Equals(
                assembly.GetName().Name,
                "Reloadable.PrivateDependency",
                StringComparison.Ordinal) &&
            AssemblyLoadContext.GetLoadContext(assembly)?.IsCollectible == true);
        Assert.NotSame(
            AssemblyLoadContext.GetLoadContext(consumerType.Assembly),
            AssemblyLoadContext.GetLoadContext(dependencyAssembly));

        Assert.Throws<InvalidOperationException>(() => AssemblyManager.BeginReload(
            [],
            [dependency.moduleName]));
        using (AssemblyReloadSession removal = AssemblyManager.BeginReload(
                   [],
                   [consumer.moduleName, dependency.moduleName]))
        {
            removal.Activate();
            _ = removal.Complete();
        }
        Assert.Empty(AssemblyManager.modules);
    }

    [Fact]
    public void CompletedReloadAllowsThePreviousContextToUnload()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");

        AssemblyUnloadMonitor monitor = CompleteReload(handle);
        ForceCollection();

        Assert.True(monitor.isCompleted);
        Assert.Equal(2, AssemblyManager.modules.Single().generation);
    }

    [Fact]
    public void RetainingOnlyTypeRefDoesNotPreventPreviousContextUnload()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        var retained = new List<TypeRef> { new(S_RELOADABLE_TYPE_ID) };

        AssemblyUnloadMonitor monitor = CompleteReload(handle);
        ForceCollection();

        Assert.True(monitor.isCompleted);
        Assert.Equal(2, ReadVersion(retained[0].Resolve()));
        GC.KeepAlive(retained);
    }

    [Fact]
    public void RetainedClrTypeKeepsUnloadPendingUntilTheHostReleasesIt()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        StrongTypeHolder holder = CreateStrongTypeHolder();

        AssemblyUnloadMonitor monitor = CompleteReload(handle);
        ForceCollection();

        Assert.Equal(AssemblyUnloadStatus.Pending, monitor.status);
        ClearStrongTypeHolder(holder);
        ForceCollection();
        Assert.Equal(AssemblyUnloadStatus.Completed, monitor.status);
        GC.KeepAlive(holder);
    }

    [Fact]
    public void RuntimeHintIsValidatedBeforeStableIdentityFallback()
    {
        _ = LoadVersion("V1");
        TypeRef target = new(S_RELOADABLE_TYPE_ID);
        TypeRef unrelated = TypeCacheManager.GetTypeRef(typeof(AssemblyManagerTests));
        ConstructorInfo constructor = typeof(TypeRef).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Guid), typeof(int)],
            modifiers: null)!;
        var mismatchedHint = (TypeRef)constructor.Invoke([target.stableId, unrelated.runtimeId]);

        Assert.Equal(target.Resolve(), mismatchedHint.Resolve());
    }

    [Fact]
    public void RolledBackCandidateRuntimeIdsAreNeverReused()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        using var registry = new ReloadableTypeRegistry();
        _ = registry.types;
        registry.failActivation = true;
        Type previous = new TypeRef(S_RELOADABLE_TYPE_ID).Resolve();
        int previousRuntimeId = TypeCacheManager.GetTypeRef(previous).runtimeId;

        using (AssemblyReloadSession failed = AssemblyManager.BeginReload(handle, CreateRequest("V2")))
            Assert.Throws<InvalidOperationException>(failed.Activate);
        int rejectedRuntimeId = registry.lastBuiltRef.runtimeId;

        registry.failActivation = false;
        using AssemblyReloadSession accepted = AssemblyManager.BeginReload(handle, CreateRequest("V2"));
        TypeCacheReloadContext context = accepted.context.GetContext<TypeCacheReloadContext>();
        TypeRef acceptedRef = context.candidate.GetTypeRef(
            new TypeRef(S_RELOADABLE_TYPE_ID).Resolve(context.candidate));
        Assert.NotEqual(previousRuntimeId, rejectedRuntimeId);
        Assert.NotEqual(rejectedRuntimeId, acceptedRef.runtimeId);
        accepted.Activate();
        _ = accepted.Complete();
    }

    [Fact]
    public void CompletionCleanupFailureDoesNotUndoPublicationOrSkipFollowingParticipants()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        var throwing = new CatalogParticipantProbe();
        var following = new CatalogParticipantProbe();
        using IDisposable throwingRegistration = AssemblyManager.RegisterCatalogParticipant(throwing);
        using IDisposable followingRegistration = AssemblyManager.RegisterCatalogParticipant(following);
        throwing.Reset();
        following.Reset();
        throwing.throwOnComplete = true;

        using AssemblyReloadSession reload = AssemblyManager.BeginReload(handle, CreateRequest("V2"));
        reload.Activate();
        _ = reload.Complete();

        Assert.Equal(1, throwing.completeCount);
        Assert.Equal(1, following.completeCount);
        Assert.Equal(2, AssemblyManager.modules.Single().generation);
        Assert.Equal(2, ReadVersion(new TypeRef(S_RELOADABLE_TYPE_ID).Resolve()));
    }

    [Fact]
    public void RollbackCleanupFailureDoesNotSkipRemainingParticipants()
    {
        _ = LoadVersion("V1");
        var observer = new CatalogParticipantProbe();
        var throwing = new CatalogParticipantProbe();
        var activationFailure = new CatalogParticipantProbe();
        using IDisposable observerRegistration = AssemblyManager.RegisterCatalogParticipant(observer);
        using IDisposable throwingRegistration = AssemblyManager.RegisterCatalogParticipant(throwing);
        using IDisposable failureRegistration = AssemblyManager.RegisterCatalogParticipant(activationFailure);
        observer.Reset();
        throwing.Reset();
        activationFailure.Reset();
        throwing.throwOnRollback = true;
        activationFailure.throwOnActivate = true;

        using AssemblyReloadSession reload = AssemblyManager.BeginReload(
            AssemblyManager.modules.Single().handle,
            CreateRequest("V2"));
        Assert.Throws<InvalidOperationException>(reload.Activate);
        activationFailure.throwOnActivate = false;
        throwing.throwOnRollback = false;

        Assert.Equal(1, observer.rollbackCount);
        Assert.Equal(1, throwing.rollbackCount);
        Assert.Equal(1, AssemblyManager.modules.Single().generation);
        Assert.Equal(1, ReadVersion(new TypeRef(S_RELOADABLE_TYPE_ID).Resolve()));
    }

    [Fact]
    public void ExternalRegistrationIsNonOwningAndImmediatelyUnloadable()
    {
        AssemblyModuleHandle handle = AssemblyManager.Register(
            "ExternalTests",
            [typeof(AssemblyManagerTests).Assembly]);

        AssemblyModuleInfo info = Assert.Single(AssemblyManager.modules);
        Assert.True(info.externallyOwned);
        Assert.False(info.collectible);

        AssemblyUnloadMonitor monitor = AssemblyManager.Unload(handle);
        Assert.True(monitor.isCompleted);
        Assert.Empty(AssemblyManager.modules);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AssemblyUnloadMonitor CompleteReload(AssemblyModuleHandle handle)
    {
        using AssemblyReloadSession reload = AssemblyManager.BeginReload(handle, CreateRequest("V2"));
        reload.Activate();
        return reload.Complete();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StrongTypeHolder CreateStrongTypeHolder()
        => new(new TypeRef(S_RELOADABLE_TYPE_ID).Resolve());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearStrongTypeHolder(StrongTypeHolder holder)
        => holder.type = null;

    private static AssemblyModuleHandle LoadVersion(string version)
        => AssemblyManager.Load(CreateRequest(version));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CaptureRejectedCandidateContext(AssemblyModuleHandle handle)
    {
        WeakReference? candidateContext = null;
        AssemblyLoadEventHandler capture = (_, args) =>
        {
            AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(args.LoadedAssembly);
            if (context is { IsCollectible: true } &&
                context.Name?.StartsWith("ReloadableTests#2", StringComparison.Ordinal) == true)
            {
                candidateContext = new WeakReference(context);
            }
        };
        AppDomain.CurrentDomain.AssemblyLoad += capture;
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => AssemblyManager.BeginReload(handle, CreateRequest("Invalid")));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyLoad -= capture;
        }
        return candidateContext ?? throw new InvalidOperationException(
            "The rejected candidate load context was not observed.");
    }

    private static AssemblyLoadRequest CreateRequest(string version)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Modules", version);
        string dependency = Path.Combine(directory, "Reloadable.PrivateDependency.dll");
        return new AssemblyLoadRequest
        {
            moduleName = "ReloadableTests",
            mainAssemblyPath = Path.Combine(directory, "Inno.Core.Assemblies.TestModule.dll"),
            preloadAssemblyPaths = File.Exists(dependency) ? [dependency] : [],
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime
        };
    }

    private static int ReadVersion(Type type)
    {
        object instance = Activator.CreateInstance(type)!;
        return (int)type.GetProperty("version")!.GetValue(instance)!;
    }

    private static void ForceCollection()
    {
        for (int i = 0; i < 8; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class ReloadableTypeRegistry : TypeRegistry<Type[]>
    {
        internal bool failActivation;
        internal TypeRef lastBuiltRef;
        internal Type[] types => current;

        protected override Type[] Build(TypeCacheSnapshot types)
        {
            TypeRef[] matches = types.types.Where(type =>
                    string.Equals(
                        type.Resolve(types).FullName,
                        "Inno.Core.Assemblies.TestModule.ReloadableExtension",
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 1)
                lastBuiltRef = matches[0];
            return matches.Select(type => type.Resolve(types)).ToArray();
        }

        protected override void OnActivating(Type[]? previous, Type[] candidate)
        {
            if (failActivation)
                throw new InvalidOperationException("Injected assembly registry activation failure.");
        }
    }

    private sealed class StrongTypeHolder(Type type)
    {
        internal Type? type = type;
    }

    private sealed class CatalogParticipantProbe : IAssemblyCatalogParticipant
    {
        internal bool throwOnActivate;
        internal bool throwOnComplete;
        internal bool throwOnRollback;
        internal int completeCount;
        internal int rollbackCount;

        public IAssemblyCatalogTransaction Prepare(AssemblyCatalogSnapshot catalog)
            => new Transaction(this);

        internal void Reset()
        {
            completeCount = 0;
            rollbackCount = 0;
        }

        private sealed class Transaction(CatalogParticipantProbe owner) : IAssemblyCatalogTransaction
        {
            public object? context => null;

            public void Activate()
            {
                if (owner.throwOnActivate)
                    throw new InvalidOperationException("Injected participant activation failure.");
            }

            public void Complete()
            {
                owner.completeCount++;
                if (owner.throwOnComplete)
                    throw new InvalidOperationException("Injected participant completion failure.");
            }

            public void Rollback()
            {
                owner.rollbackCount++;
                if (owner.throwOnRollback)
                    throw new InvalidOperationException("Injected participant rollback failure.");
            }
        }
    }
}
