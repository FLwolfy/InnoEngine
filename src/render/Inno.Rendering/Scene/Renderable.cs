
namespace Inno.Rendering;

/// <summary>
/// Represents a renderable scene object.
/// </summary>
public abstract class Renderable
{
    public string name { get; set; } = string.Empty;

    public Transform transform { get; set; } = Transform.identity;

    public uint layerMask { get; set; } = uint.MaxValue;

    public Visibility visibility { get; set; } = Visibility.Visible;

    public ShadowMode shadowMode { get; set; } = ShadowMode.CastAndReceive;

    public MotionMode motionMode { get; set; } = MotionMode.Static;

    public int sortingOrder { get; set; }
}
