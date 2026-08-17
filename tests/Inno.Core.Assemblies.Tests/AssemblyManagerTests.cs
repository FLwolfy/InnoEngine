using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

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
    }

    public void Dispose()
    {
        AssemblyManager.Shutdown();
        ForceCollection();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void LoadAndReloadPublishOnlyTheActiveGeneration()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        Assert.True(TypeCache.TryResolveType(S_RELOADABLE_TYPE_ID, out Type? previous));
        Assert.Equal(1, ReadVersion(previous!));
        Assert.True(TypeCache.TryGetRuntimeTypeId(previous!, out int previousRuntimeId));

        using AssemblyReloadSession reload = AssemblyManager.BeginReload(handle, CreateRequest("V2"));
        Assert.True(reload.context.TryResolveReplacement(previous!, out Type? candidate));
        Assert.NotSame(previous, candidate);
        Assert.Same(previous, TypeCache.current.types.Single(type => type == previous));

        reload.Activate();

        Assert.True(TypeCache.TryResolveType(S_RELOADABLE_TYPE_ID, out Type? current));
        Assert.Same(candidate, current);
        Assert.Equal(2, ReadVersion(current!));
        Assert.True(TypeCache.TryGetRuntimeTypeId(current!, out int currentRuntimeId));
        Assert.NotEqual(previousRuntimeId, currentRuntimeId);
        _ = reload.Complete();
        Assert.Throws<InvalidOperationException>(() => _ = reload.context.previousTypes);
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
        Assert.True(TypeCache.TryResolveType(S_RELOADABLE_TYPE_ID, out Type? restored));
        Assert.Same(previous, restored);
    }

    [Fact]
    public void InvalidCandidateLeavesTheActiveGenerationUntouched()
    {
        AssemblyModuleHandle handle = LoadVersion("V1");
        Assert.True(TypeCache.TryResolveType(S_RELOADABLE_TYPE_ID, out Type? previous));

        Assert.Throws<InvalidOperationException>(
            () => AssemblyManager.BeginReload(handle, CreateRequest("Invalid")));

        Assert.True(TypeCache.TryResolveType(S_RELOADABLE_TYPE_ID, out Type? current));
        Assert.Same(previous, current);
        Assert.Equal(1, AssemblyManager.modules.Single().generation);
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

    private static AssemblyModuleHandle LoadVersion(string version)
        => AssemblyManager.Load(CreateRequest(version));

    private static AssemblyLoadRequest CreateRequest(string version)
        => new()
        {
            moduleName = "ReloadableTests",
            mainAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                "Modules",
                version,
                "Inno.Core.Assemblies.TestModule.dll")
        };

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
        internal Type[] types => current;

        protected override Type[] Build(TypeCacheSnapshot types)
            => types.types.Where(type =>
                    string.Equals(
                        type.FullName,
                        "Inno.Core.Assemblies.TestModule.ReloadableExtension",
                        StringComparison.Ordinal))
                .ToArray();
    }
}
