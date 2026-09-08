using System;
using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Scene;

namespace Inno.Scene.Reload.TestModule;

/// <summary>
/// Provides reloadable scene state for validating Plugin generation removal and recovery.
/// </summary>
[StableTypeId("9f67d41e-082b-46d5-aaf0-dfc76c693182")]
public sealed class ReloadableSceneComponent : GameComponent
{
    /// <summary>
    /// Gets or sets the state that must survive a temporarily unavailable Plugin generation.
    /// </summary>
    [SerializableProperty]
    public int value { get; set; }

    /// <summary>
    /// Gets or sets an asset reference whose persistent identity survives module removal.
    /// </summary>
    [SerializableProperty]
    public TextAsset? asset { get; set; }

    /// <summary>
    /// Gets or sets whether restored state violates this component's domain validation rule.
    /// </summary>
    [SerializableProperty]
    public bool rejectRestore { get; set; }

    [OnSerializableRestored]
    private void ValidateRestoredState()
    {
        if (rejectRestore)
            throw new InvalidOperationException("Component state rejected restoration.");
    }
}
