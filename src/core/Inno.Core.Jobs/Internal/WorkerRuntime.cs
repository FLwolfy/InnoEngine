using System.Threading;

namespace Inno.Core.Jobs.Internal;

internal sealed class WorkerRuntime
{
    internal WorkerRuntime(int workerId, ThreadStart loopBody)
    {
        id = workerId;
        localQueue = new WorkStealingDeque<int>();
        thread = new Thread(loopBody)
        {
            IsBackground = true,
            Name = $"Inno.JobWorker.{workerId}"
        };
    }

    internal int id { get; }
    internal Thread thread { get; }
    internal WorkStealingDeque<int> localQueue { get; }
}
