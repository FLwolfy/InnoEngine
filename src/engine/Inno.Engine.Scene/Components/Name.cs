using Inno.Core.ECS;

namespace Inno.Engine.Scene.Components;

/// <summary>
/// Stores the display name of an entity in a scene.
/// </summary>
internal sealed class Name : Component
{
    private const string DEFAULT_NAME = "GameObject";

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string value { get; set; } = DEFAULT_NAME;

    /// <inheritdoc />
    public override void Reset()
    {
        value = DEFAULT_NAME;
    }
}
