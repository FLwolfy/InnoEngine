using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Represents scene-level rendering settings.
/// </summary>
public sealed class SceneRenderSettings
{
    public bool enableShadows { get; set; }

    public bool enableFog { get; set; }
}
