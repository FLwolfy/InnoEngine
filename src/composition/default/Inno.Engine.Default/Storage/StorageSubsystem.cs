using System.IO;
using Inno.Runtime.Contracts;
using Inno.Storage.Runtime;

namespace Inno.Engine.Default;

internal static class StorageSubsystem
{
    [RuntimeSubsystemRegistration("inno.runtime.storage")]
    internal static IRuntimeSubsystemFactory Create(EngineSessionComposition context)
        => new StorageRuntimeFactory(owner => context.adapters.storage.CreateStorage(context.selection.storage,
            Path.Combine(owner.persistentDataDirectory, "Storage")));
}
