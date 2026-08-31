namespace Inno.Core.Job.Internal;

internal enum JobExecutionState : byte
{
    None = 0,
    Created = 1,
    Queued = 2,
    Running = 3,
    Completed = 4
}
