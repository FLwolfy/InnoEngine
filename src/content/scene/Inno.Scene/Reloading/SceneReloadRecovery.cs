using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets;
using Inno.Core.Execution;
using Inno.Extensibility.Types;
using Inno.References;

namespace Inno.Scene;

internal sealed class SceneReloadRecovery : ISceneReloadStateTransfer
{
    private readonly ReferenceRecoveryTransaction m_recovery;
    private SceneReloadStateTransfer? m_transfer;
    private IReadOnlyList<SceneReloadDiagnostic> m_diagnostics = Array.Empty<SceneReloadDiagnostic>();

    internal SceneReloadRecovery(SceneReloadStateTransfer transfer, SceneWorld world,
        TypeCacheReloadContext context, IAssetReferenceResolver assets)
    {
        m_transfer = transfer;
        m_recovery = new ReferenceRecoveryTransaction(
            ReferenceCatalog.Create(context.candidate.version,
                [new SceneElementReferenceResolver(world, context.candidate), assets]),
            transfer.CaptureRecoveryStates(), [transfer]);
    }

    IReadOnlyList<object> ISceneReloadStateTransfer.retiredObjects => m_transfer?.retiredObjects ?? Array.Empty<object>();
    IReadOnlyList<SceneReloadDiagnostic> ISceneReloadStateTransfer.diagnostics => m_diagnostics;
    IReadOnlyList<ReferenceRecoveryChange> ISceneReloadStateTransfer.recoveryChanges => m_recovery.changes;
    void ISceneReloadStateTransfer.PrepareForActivation() => m_recovery.PrepareForActivation();
    void ISceneReloadStateTransfer.Apply()
    {
        m_recovery.Apply();
        CaptureDiagnostics();
    }
    void ISceneReloadStateTransfer.RollbackStructure() => m_recovery.RollbackStructure();
    void ISceneReloadStateTransfer.RestorePreviousState() => FinalizeRecovery(restore: true);
    void ISceneReloadStateTransfer.Complete() => FinalizeRecovery(restore: false);

    private void FinalizeRecovery(bool restore)
    {
        bool pending = false;
        try
        {
            if (restore)
                m_recovery.RestorePreviousState();
            else
                m_recovery.Complete();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            pending = true;
            throw;
        }
        finally
        {
            CaptureDiagnostics();
            if (!pending)
                m_transfer = null;
        }
    }

    private void CaptureDiagnostics()
    {
        if (m_transfer is not null)
            m_diagnostics = Array.AsReadOnly(m_transfer.diagnostics.ToArray());
    }
}
