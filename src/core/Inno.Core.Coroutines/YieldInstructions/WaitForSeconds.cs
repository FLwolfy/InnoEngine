namespace Inno.Core.Coroutines;

/// <summary>
/// Waits for the specified number of scaled seconds.
/// </summary>
public sealed class WaitForSeconds(float seconds) : YieldInstruction
{
    /// <summary>
    /// Gets the configured wait time in seconds.
    /// </summary>
    public float seconds { get; } = seconds;

    internal override CoroutineWaitDelegate CreateWaiter(double now, ulong frame)
    {
        ulong targetFrame = frame + 1;
        if (seconds <= 0f)
        {
            return scheduler => scheduler.frame < targetFrame;
        }

        double targetTime = now + seconds;
        return scheduler => scheduler.now < targetTime;
    }
}
