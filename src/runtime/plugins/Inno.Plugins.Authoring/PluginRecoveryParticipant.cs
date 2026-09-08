using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Assets.Pipeline;
using Inno.Core.Settings;
using Inno.Extensibility.Reload;
using Inno.References;

namespace Inno.Plugins.Authoring;

internal sealed class PluginRecoveryParticipant : IReferenceRecoveryParticipant
{
    private readonly PluginEnvironment m_owner;
    private readonly AssetPipeline m_assets;
    private readonly IGenerationChange m_settings;
    private bool m_prepared;

    internal PluginRecoveryParticipant(PluginEnvironment owner, AssetPipeline assets, ProjectSettingsStore settings)
    {
        m_owner = owner;
        m_assets = assets;
        m_settings = settings.CreateReloadChange();
    }

    /// <summary>
    /// Prepares candidate state without changing the active generation.
    /// </summary>
    public void PrepareForActivation()
    {
        m_settings.PrepareForActivation();
        m_prepared = true;
    }

    /// <summary>
    /// Applies the prepared state at the caller-controlled commit point.
    /// </summary>
    public void Apply()
    {
        m_owner.ActivatePending();
        m_assets.Update();
        m_settings.Apply();
    }

    /// <summary>
    /// Rejects duplicate Plugin sources after the complete content candidate has been applied.
    /// </summary>
    /// <param name="changes">
    /// Resolved object-slot changes; Asset references are validated by the nested source-mount transaction.
    /// </param>
    public void Validate(IReadOnlyList<ReferenceRecoveryChange> changes)
    {
        if (m_owner.activePlugins.Select(static plugin => plugin.sourceMount.id).Distinct().Count()
            != m_owner.activePlugins.Count)
            throw new InvalidOperationException("Plugin publication contains duplicate source identities.");
        // Setting and Plugin IDs identify protocols, not live objects. Asset slots are validated by the mount
        // transaction; composing settings above restores their references with the owner's complete context.
    }

    /// <summary>
    /// Completes the committed operation and releases temporary state.
    /// </summary>
    public void Complete()
    {
        m_owner.CommitPending();
        m_settings.Complete();
    }

    /// <summary>
    /// Restores the state that existed before candidate activation began.
    /// </summary>
    public void RollbackStructure() => m_settings.RollbackStructure();

    /// <summary>
    /// Restores the state that existed before candidate activation began.
    /// </summary>
    public void RestorePreviousState()
    {
        m_owner.RollbackPending();
        if (!m_prepared) return;
        m_assets.Update();
        m_settings.RestorePreviousState();
    }
}
