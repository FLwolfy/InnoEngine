
namespace Inno.Rendering;

/// <summary>
/// Represents a window description used by backbuffer targets.
/// </summary>
public sealed class RenderWindow
{
    public required IntPtr nativeHandle { get; init; }

    public int width { get; set; }

    public int height { get; set; }
}
