using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Scene;

namespace Inno.Audio.Scene;

/// <summary>
/// Declares a scene listener candidate selected deterministically by priority and persistent identity.
/// </summary>
[StableTypeId("8660a080-84c4-4f21-a8c3-f6fa70bc5f51")]
public sealed class AudioListener : GameBehavior
{
    /// <summary>
    /// Gets or sets listener selection priority; larger values win.
    /// </summary>
    [SerializableProperty]
    public int priority { get; set; }
}
