using System;

using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Runtime;
using Inno.Scene;

namespace Inno.Editor.Scene;

internal static class SceneReloadIntegration
{
    internal static IDisposable Acquire(
        RuntimeSession runtimeSession,
        SerializationRegistry serialization,
        EditorReloadCoordinator reloads)
    {
        ArgumentNullException.ThrowIfNull(runtimeSession);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(reloads);
        var participant = new SceneReloadParticipant(runtimeSession, serialization);
        return new Registration(reloads.Register(participant));
    }

    private sealed class SceneReloadParticipant : IEditorReloadParticipant
    {
        private readonly RuntimeSession m_runtimeSession;
        private readonly SceneReloadService m_reload;

        internal SceneReloadParticipant(
            RuntimeSession runtimeSession,
            SerializationRegistry serialization)
        {
            m_runtimeSession = runtimeSession;
            m_reload = new SceneReloadService(runtimeSession.scenes, serialization);
        }

        /// <summary>
        /// Captures an immutable snapshot of the current observable state.
        /// </summary>
        /// <param name="context">
        /// The operation scope that provides state, services, and ownership boundaries.
        /// </param>
        /// <returns>
        /// The validated ieditor reload transaction that represents the completed operation.
        /// </returns>
        public IEditorReloadTransaction Capture(AssemblyReloadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            TypeCacheReloadContext typeReload = context.GetContext<TypeCacheReloadContext>();
            return new SceneReloadTransaction(
                m_runtimeSession,
                m_reload.Capture(typeReload));
        }

        /// <summary>
        /// Recomputes diagnostics from the current candidate without publishing partial state.
        /// </summary>
        public void RefreshDiagnostics()
            => SceneStateDiagnosticPublisher.PublishMissingElements(
                m_runtimeSession.scenes.loadedScenes);
    }

    private sealed class SceneReloadTransaction(
        RuntimeSession runtimeSession,
        ISceneReloadStateTransfer migration) : IEditorReloadTransaction
    {
        /// <summary>
        /// Builds and validates candidate state without changing the active generation.
        /// </summary>
        public void PrepareForActivation()
        {
            migration.PrepareForActivation();
            foreach (object retiredObject in migration.retiredObjects)
                runtimeSession.StopCoroutines(retiredObject);
        }

        /// <summary>
        /// Applies a validated change atomically at the caller-controlled commit point.
        /// </summary>
        public void Apply()
            => migration.Apply();

        /// <summary>
        /// Finalizes candidate activation and releases temporary transaction state.
        /// </summary>
        public void Complete()
        {
            migration.Complete();
            SceneStateDiagnosticPublisher.PublishReload(migration.diagnostics);
            SceneStateDiagnosticPublisher.PublishMissingElements(
                runtimeSession.scenes.loadedScenes);
        }

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void RollbackStructure()
            => migration.RollbackStructure();

        /// <summary>
        /// Restores the state captured before the current transaction began.
        /// </summary>
        public void RestorePreviousState()
        {
            migration.RestorePreviousState();
            SceneStateDiagnosticPublisher.PublishMissingElements(
                runtimeSession.scenes.loadedScenes);
        }
    }

    private sealed class Registration(IDisposable registration) : IDisposable
    {
        private IDisposable? m_registration = registration;

        /// <summary>
        /// Unregisters Scene migration and clears diagnostics owned by that integration.
        /// </summary>
        public void Dispose()
        {
            IDisposable? current = m_registration;
            if (current is null)
                return;
            m_registration = null;
            try
            {
                current.Dispose();
            }
            finally
            {
                SceneStateDiagnosticPublisher.ClearAll();
            }
        }
    }

}
