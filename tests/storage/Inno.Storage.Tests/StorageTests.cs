using Inno.Runtime.Contracts;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inno.Core.Execution;

using Inno.Adapter.Storage.FileSystem;
using Inno.Storage.Runtime;
using Inno.Runtime;
using Xunit;

namespace Inno.Storage.Tests;

public sealed class StorageTests
{
    [Fact]
    public async Task AdmissionRejectsBeforeBackendAndAcceptedWriteOwnsItsBytes()
    {
        var backend = new PendingStorage();
        using var runtime = new StorageRuntime(backend, maxPendingOperations: 1, maxWriteBytes: 4);
        byte[] source = [1, 2, 3];
        Task write = runtime.WriteAsync(new StorageKey("save"), source).AsTask();
        source[0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, backend.value.ToArray());
        Assert.Throws<InvalidOperationException>(() => runtime.ExistsAsync(new StorageKey("save")));
        Assert.Equal(1, runtime.pendingOperations);
        Assert.Equal(1, runtime.rejectedOperations);
        backend.completion.SetResult();
        await write;
        Assert.False(await runtime.ExistsAsync(new StorageKey("save")));
        Assert.Equal(0, runtime.pendingOperations);
    }

    [Fact]
    public async Task RuntimeCancellationRetainsBackendUntilAcceptedWriteCompletes()
    {
        var backend = new PendingStorage();
        using var runtime = new StorageRuntime(backend);
        Task write = runtime.WriteAsync(new StorageKey("save"), new byte[] { 1 }).AsTask();
        Assert.Throws<RetirementPendingException>(runtime.Dispose);
        Assert.True(backend.token.IsCancellationRequested);
        Assert.False(backend.disposed);
        Assert.Throws<InvalidOperationException>(() => runtime.WriteAsync(new StorageKey("later"), new byte[] { 2 }));
        backend.completion.SetResult();
        await write;
        runtime.Dispose();
        Assert.True(backend.disposed);
    }

    [Fact]
    public async Task DirectAdapterDisposalDoesNotDestroyAnActiveWritesSemaphore()
    {
        string root = CreateTemporaryRoot();
        try
        {
            using var storage = new FileSystemApplicationStorage(root);
            Task write = storage.WriteAsync(new StorageKey("large"), new byte[8 * 1024 * 1024]).AsTask();
            storage.Dispose();
            await write;
            Assert.Equal(8 * 1024 * 1024, new FileInfo(Path.Combine(root, "large")).Length);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemAdapterAtomicallyStoresAndListsValues()
    {
        string root = CreateTemporaryRoot();
        try
        {
            using var storage = new FileSystemApplicationStorage(root);
            var first = new StorageKey("saves/slot-1.bin");
            var second = new StorageKey("settings/user.bin");

            await storage.WriteAsync(first, new byte[] { 1, 2, 3 });
            await storage.WriteAsync(second, new byte[] { 4 });

            Assert.Equal(new byte[] { 1, 2, 3 }, await storage.ReadAsync(first));
            Assert.Equal([first], await storage.ListAsync(new StorageKey("saves")));
            Assert.True(await storage.DeleteAsync(first));
            Assert.False(await storage.ExistsAsync(first));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StorageKeyRejectsEscapingAndAmbiguousPaths()
    {
        Assert.Throws<ArgumentException>(() => new StorageKey("../outside"));
        Assert.Throws<ArgumentException>(() => new StorageKey("root//value"));
        Assert.Throws<ArgumentException>(() => new StorageKey("/absolute"));
        Assert.Throws<ArgumentException>(() => new StorageKey("C:/absolute"));
    }

    [Fact]
    public void ExecutionScopesRequireLastInFirstOutDisposal()
    {
        string firstRoot = CreateTemporaryRoot();
        string secondRoot = CreateTemporaryRoot();
        try
        {
            using var first = new FileSystemApplicationStorage(firstRoot);
            using var second = new FileSystemApplicationStorage(secondRoot);
            IDisposable outer = StorageExecutionContext.EnterScope(first);
            IDisposable inner = StorageExecutionContext.EnterScope(second);
            Assert.Same(second, StorageExecutionContext.current);
            Assert.Throws<InvalidOperationException>(outer.Dispose);
            inner.Dispose();
            outer.Dispose();
            Assert.Throws<InvalidOperationException>(() => _ = StorageExecutionContext.current);
        }
        finally
        {
            if (Directory.Exists(firstRoot))
                Directory.Delete(firstRoot, recursive: true);
            if (Directory.Exists(secondRoot))
                Directory.Delete(secondRoot, recursive: true);
        }
    }

    [Fact]
    public void RuntimeSubsystemBindsStorageForTheCompleteFrame()
    {
        string root = CreateTemporaryRoot();
        try
        {
            using EngineHost host = new EngineHostBuilder()
                .UseMetadataCache(Path.Combine(root, "Metadata"))
                .Build();
            var options = new RuntimeSessionOptions
            {
                kind = RuntimeSessionKind.Play,
                applicationId = "tests.storage",
                persistentDataDirectory = Path.Combine(root, "tests.storage"),
                jobExecutionMode = RuntimeJobExecutionMode.SingleThread,
                createSubsystems = owner =>
                [
                    new StorageRuntimeFactory(context =>
                        new FileSystemApplicationStorage(Path.Combine(
                            context.persistentDataDirectory,
                            "Storage"))),
                    new StorageProbeFactory()
                ]
            };

            using (RuntimeSession session = host.CreateSession(options))
                session.Tick(0.01f);

            Assert.Throws<InvalidOperationException>(() => _ = StorageExecutionContext.current);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
        => Path.Combine(Path.GetTempPath(), "InnoStorageTests", Guid.NewGuid().ToString("N"));

    private sealed class StorageProbeFactory : IRuntimeSubsystemFactory
    {
        public RuntimeSubsystemDescriptor descriptor { get; } = new(
            new RuntimeSubsystemId("tests.storage.probe"),
            dependencies: [new RuntimeSubsystemId("inno.runtime.storage")]);

        public IRuntimeSubsystem Create(RuntimeSubsystemContext context) => new StorageProbe();
    }

    private sealed class StorageProbe : RuntimeSubsystem
    {
        protected override void OnUpdate(RuntimeFrame frame)
            => Assert.NotNull(StorageExecutionContext.current);
    }

    private sealed class PendingStorage : IApplicationStorage, IDisposable
    {
        internal TaskCompletionSource completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken token { get; private set; }
        internal bool disposed { get; private set; }
        internal ReadOnlyMemory<byte> value { get; private set; }
        public ValueTask WriteAsync(StorageKey key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default)
        {
            token = cancellationToken;
            this.value = value;
            return new ValueTask(completion.Task);
        }
        public ValueTask<bool> ExistsAsync(StorageKey key, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<byte[]?> ReadAsync(StorageKey key, CancellationToken cancellationToken = default) => ValueTask.FromResult<byte[]?>(null);
        public ValueTask<bool> DeleteAsync(StorageKey key, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<IReadOnlyList<StorageKey>> ListAsync(StorageKey? prefix = null, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<StorageKey>>(Array.Empty<StorageKey>());
        public void Dispose() => disposed = true;
    }
}
