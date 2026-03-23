using Inno.Graphics;

namespace Inno.Graphics;

/// <summary>
/// Describes swapchain creation and presentation behavior.
/// </summary>
public sealed class GraphicsSwapchainDescription
{
    public required IntPtr nativeHandle { get; init; }

    public int width { get; init; }

    public int height { get; init; }

    public PixelFormat colorFormat { get; init; } = PixelFormat.B8G8R8A8Unorm;

    public PixelFormat depthFormat { get; init; } = PixelFormat.D24UnormS8Uint;

    public SampleCount sampleCount { get; init; } = SampleCount.Count1;

    public bool vSync { get; init; } = true;
}
