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

    internal override ICoroutineWaiter CreateWaiter(double now, ulong frame)
    {
        if (seconds <= 0f)
        {
            return new NextFrameWaiter(frame + 1);
        }

        return new WaitForSecondsWaiter(now + seconds);
    }
}
