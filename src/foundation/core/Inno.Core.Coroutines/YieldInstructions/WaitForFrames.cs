namespace Inno.Core.Coroutines;

/// <summary>
/// Waits for a fixed number of frames.
/// </summary>
/// <param name="frames">
/// The frames used to initialize this instance.
/// </param>
public sealed class WaitForFrames(int frames) : YieldInstruction
{
    /// <summary>
    /// Gets the configured frame count.
    /// </summary>
    public int frames { get; } = frames;

    internal override CoroutineWaitDelegate CreateWaiter(double now, ulong frame)
    {
        ulong targetFrame = frame + 1;
        if (frames <= 0)
        {
            return scheduler => scheduler.frame < targetFrame;
        }

        ulong target = frame + (ulong)frames;
        return scheduler => scheduler.frame < target;
    }
}
