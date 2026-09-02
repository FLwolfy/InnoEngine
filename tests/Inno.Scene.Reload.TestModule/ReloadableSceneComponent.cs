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
}
