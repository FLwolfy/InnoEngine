using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

using Inno.Core.Jobs;


namespace Inno.Core.JobsSystem.Tests;

public sealed class JobSystemBehaviorTests
{
    [Fact]
    public void SingleThread_ExecutesDeterministicallyOnMainThread()
    {
        using var jobs = new JobScheduler(JobExecutionMode.SingleThread);
        jobs.BeginFrame();

        var mainThread = Environment.CurrentManagedThreadId;
        var observed = new List<int>();
        var a = jobs.Schedule(() => observed.Add(Environment.CurrentManagedThreadId));
        var b = jobs.Schedule(() => observed.Add(Environment.CurrentManagedThreadId));

        jobs.CompleteAll(stackalloc[] { a, b });
        jobs.EndFrame();

        Assert.Equal(2, observed.Count);
        Assert.All(observed, id => Assert.Equal(mainThread, id));
        Assert.Equal(0, jobs.workerCount);
    }

    [Fact]
    public void SingleThread_RejectsCrossThreadUsage()
    {
        using var jobs = new JobScheduler(JobExecutionMode.SingleThread);
        jobs.BeginFrame();

        Exception? fault = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = jobs.Schedule(() => { });
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        });
        thread.Start();
        thread.Join();

        jobs.EndFrame();
        Assert.IsType<InvalidOperationException>(fault);
    }

    [Fact]
    public void WorkStealing_ScheduleAndComplete_RunsJob()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        var value = 0;
        var handle = jobs.Schedule(() => value = 7);
        jobs.Complete(handle);
        jobs.EndFrame();

        Assert.Equal(7, value);
    }

    [Fact]
    public void WorkStealing_ScheduleWithDependencies_RunsInOrder()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        var order = new ConcurrentQueue<int>();
        var first = jobs.Schedule(() => order.Enqueue(1));
        var second = jobs.Schedule(_ => order.Enqueue(2), null, [first]);

        jobs.Complete(second);
        jobs.EndFrame();

        Assert.Equal(new[] { 1, 2 }, order.ToArray());
    }

    [Fact]
    public void WorkStealing_CombineDependencies_CompletesAfterAll()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        var sum = 0;
        var a = jobs.Schedule(() => Interlocked.Add(ref sum, 3));
        var b = jobs.Schedule(() => Interlocked.Add(ref sum, 4));
        var barrier = jobs.CombineDependencies([a, b]);

        jobs.Complete(barrier);
        jobs.EndFrame();

        Assert.Equal(7, sum);
    }

    [Fact]
    public void WorkStealing_ParallelFor_CoversEveryIndexExactlyOnce()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        const int length = 1024;
        var values = new int[length];
        var handle = jobs.ParallelFor(length, 32, (start, end) =>
        {
            for (var i = start; i < end; i++)
            {
                Interlocked.Increment(ref values[i]);
            }
        });

        jobs.Complete(handle);
        jobs.EndFrame();

        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(1, values[i]);
        }
    }

    [Fact]
    public void WorkStealing_MainThreadQueue_DrainExecutesOnMainThread()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        var mainThread = Environment.CurrentManagedThreadId;
        var threadIds = new List<int>();
        jobs.EnqueueMainThread(() => threadIds.Add(Environment.CurrentManagedThreadId));
        jobs.EnqueueMainThread(() => threadIds.Add(Environment.CurrentManagedThreadId));
        jobs.DrainMainThreadQueue();
        jobs.EndFrame();

        Assert.Equal(2, threadIds.Count);
        Assert.All(threadIds, id => Assert.Equal(mainThread, id));
    }

    [Fact]
    public void WorkStealing_MainThreadQueue_DrainFromWorkerThread_Throws()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        Exception? fault = null;
        var thread = new Thread(() =>
        {
            try
            {
                jobs.DrainMainThreadQueue();
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        });
        thread.Start();
        thread.Join();

        jobs.EndFrame();
        Assert.IsType<InvalidOperationException>(fault);
    }

    [Fact]
    public void WorkStealing_WhenJobThrows_CompleteRethrowsAndEndFrameAggregates()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        var handle = jobs.Schedule(() => throw new InvalidOperationException("boom"));
        var completeEx = Assert.Throws<InvalidOperationException>(() => jobs.Complete(handle));
        Assert.IsType<InvalidOperationException>(completeEx.InnerException);

        var endEx = Assert.Throws<AggregateException>(() => jobs.EndFrame());
        Assert.NotEmpty(endEx.InnerExceptions);
    }

    [Fact]
    public void WorkStealing_EndFrameRecyclesHandle_AndStaleHandleThrows()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        var handle = jobs.Schedule(() => { });
        jobs.EndFrame();

        jobs.BeginFrame();
        var ex = Assert.Throws<InvalidOperationException>(() => jobs.Complete(handle));
        jobs.EndFrame();

        Assert.Contains("stale", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkStealing_ConcurrentSchedulingAcrossThreads_IsStable()
    {
        using var jobs = CreateWorkStealing(workerCount: 4);
        jobs.BeginFrame();

        const int taskCount = 2000;
        var count = 0;
        Parallel.For(0, taskCount, _ =>
        {
            var handle = jobs.Schedule(() => Interlocked.Increment(ref count));
            jobs.Complete(handle);
        });

        jobs.EndFrame();
        Assert.Equal(taskCount, count);
    }

    [Fact]
    public void WorkStealing_WorkerThreadsParticipateUnderLoad()
    {
        using var jobs = CreateWorkStealing(workerCount: 4);
        jobs.BeginFrame();

        var threadIds = new ConcurrentDictionary<int, byte>();
        var handles = new JobHandle[256];
        for (var i = 0; i < handles.Length; i++)
        {
            handles[i] = jobs.Schedule(() =>
            {
                threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                Thread.Sleep(1);
            });
        }

        jobs.CompleteAll(handles);
        jobs.EndFrame();

        Assert.True(threadIds.Count >= 2);
    }

    [Fact]
    public void WorkStealing_NestedScheduleFromWorker_IsSupported()
    {
        using var jobs = CreateWorkStealing(workerCount: 2);
        jobs.BeginFrame();

        var value = 0;
        var outer = jobs.Schedule(() =>
        {
            var inner = jobs.Schedule(() => Interlocked.Add(ref value, 3));
            jobs.Complete(inner);
            Interlocked.Add(ref value, 2);
        });

        jobs.Complete(outer);
        jobs.EndFrame();

        Assert.Equal(5, value);
    }

    [Fact]
    public void WorkStealing_AutoWorkerCount_IsInValidRange()
    {
        using var jobs = new JobScheduler(
            JobExecutionMode.WorkerPool,
            new JobSchedulerOptions { workerCount = 0 });
        Assert.InRange(jobs.workerCount, 1, 64);
    }

    [Fact]
    public void WorkStealing_CombineManyDependencies_CompletesReliably()
    {
        using var jobs = CreateWorkStealing(workerCount: 4);
        jobs.BeginFrame();

        const int count = 300;
        var handles = new JobHandle[count];
        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            handles[i] = jobs.Schedule(() => Interlocked.Increment(ref sum));
        }

        var barrier = jobs.CombineDependencies(handles);
        jobs.Complete(barrier);
        jobs.EndFrame();

        Assert.Equal(count, sum);
    }

    [Fact]
    public void CommonApi_EndFrameWithoutBeginFrame_Throws()
    {
        using var jobs = CreateWorkStealing();
        Assert.Throws<InvalidOperationException>(() => jobs.EndFrame());
    }

    [Fact]
    public void CommonApi_BeginFrameTwice_Throws()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => jobs.BeginFrame());
        jobs.EndFrame();
    }

    [Fact]
    public void CommonApi_ParallelForInvalidArguments_Throws()
    {
        using var jobs = CreateWorkStealing();
        jobs.BeginFrame();

        Assert.Throws<ArgumentOutOfRangeException>(() => jobs.ParallelFor(-1, 8, (_, _) => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => jobs.ParallelFor(1, 0, (_, _) => { }));

        jobs.EndFrame();
    }

    [Fact]
    public void CommonApi_DisposePreventsFurtherUse()
    {
        var jobs = CreateWorkStealing();
        jobs.Dispose();

        Assert.Throws<ObjectDisposedException>(() => jobs.BeginFrame());
        Assert.Throws<ObjectDisposedException>(() => jobs.EnqueueMainThread(() => { }));
    }

    private static JobScheduler CreateWorkStealing(int workerCount = 2)
    {
        return new JobScheduler(JobExecutionMode.WorkerPool, new JobSchedulerOptions
        {
            workerCount = workerCount
        });
    }
}
