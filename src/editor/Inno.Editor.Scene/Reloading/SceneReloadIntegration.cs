using System;

using Inno.Core.Assemblies;
using Inno.Core.Framework;
using Inno.Core.Reflection;
using Inno.Editor.Core;
using Inno.Engine.Scene.Assets;

namespace Inno.Editor.Scene;

internal static class SceneReloadIntegration
{
    private static readonly object S_SYNC = new();
    private static readonly SceneReloadParticipant S_PARTICIPANT = new();

    private static IDisposable? s_registration;
    private static int s_referenceCount;

    internal static IDisposable Acquire()
    {
        lock (S_SYNC)
        {
            if (s_referenceCount == 0)
                s_registration = EditorReloadCoordinator.Register(S_PARTICIPANT);
            s_referenceCount++;
            return new Lease();
        }
    }

    private static void Release()
    {
        lock (S_SYNC)
        {
            if (s_referenceCount == 0)
                return;
            s_referenceCount--;
            if (s_referenceCount != 0)
                return;
            s_registration?.Dispose();
            s_registration = null;
            SceneStateDiagnosticPublisher.ClearAll();
        }
    }

    private sealed class SceneReloadParticipant : IEditorReloadParticipant
    {
        public IEditorReloadTransaction Capture(AssemblyReloadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            TypeCacheReloadContext typeReload = context.GetContext<TypeCacheReloadContext>();
            return new SceneReloadTransaction(SceneReloadService.Capture(typeReload));
        }

        public void RefreshDiagnostics()
            => SceneStateDiagnosticPublisher.PublishMissingElements();
    }

    private sealed class SceneReloadTransaction(ISceneReloadMigration migration) : IEditorReloadTransaction
    {
        public void PrepareForActivation()
        {
            migration.PrepareForActivation();
            if (!Shell.isInitialized)
                return;
            foreach (object retiredObject in migration.retiredObjects)
                Shell.instance.coroutineScheduler.StopAllCoroutines(retiredObject);
        }

        public void Apply()
            => migration.Apply();

        public void Complete()
        {
            migration.Complete();
            SceneStateDiagnosticPublisher.PublishReload(migration.diagnostics);
            SceneStateDiagnosticPublisher.PublishMissingElements();
        }

        public void RollbackStructure()
            => migration.RollbackStructure();

        public void RestorePreviousState()
        {
            migration.RestorePreviousState();
            SceneStateDiagnosticPublisher.PublishMissingElements();
        }
    }

    private sealed class Lease : IDisposable
    {
        private bool m_disposed;

        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            Release();
        }
    }
}
