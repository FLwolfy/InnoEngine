namespace Inno.Core.Coroutines;

/// <summary>
/// Waits for a fixed number of frames.
/// </summary>
public sealed class WaitForFrames(int frames) : YieldInstruction
{
    /// <summary>
    /// Gets the configured frame count.
    /// </summary>
    public int frames { get; } = frames;

    internal override ICoroutineWaiter CreateWaiter(double now, ulong frame)
    {
        if (frames <= 0)
        {
            return new NextFrameWaiter(frame + 1);
        }

        return new WaitForFramesWaiter(frame + (ulong)frames);
    }
}
