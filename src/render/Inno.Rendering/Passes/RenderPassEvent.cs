
namespace Inno.Rendering;

/// <summary>
/// Defines pass scheduling points in the frame.
/// </summary>
public enum RenderPassEvent
{
    BeforeDepthPrepass = 100,
    DepthPrepass = 200,
    BeforeShadows = 300,
    Shadows = 400,
    BeforeOpaque = 500,
    Opaque = 600,
    Skybox = 700,
    BeforeTransparent = 800,
    Transparent = 900,
    BeforePostProcess = 1000,
    PostProcess = 1100,
    BeforeUi = 1200,
    Ui = 1300,
    AfterFrame = 1400
}
