using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Scene;

namespace Inno.Scene.Reload.TestModule;

/// <summary>
/// Provides persistent system state for exercising missing-system History and reload recovery.
/// </summary>
[StableTypeId("92dd722d-f4ce-468d-8f6d-056cf5b1f79b")]
public sealed class ReloadableSceneSystem : GameSystem
{
    /// <summary>
    /// Gets or sets the state retained while the system implementation is unavailable.
    /// </summary>
    [SerializableProperty]
    public int value { get; set; }
}
