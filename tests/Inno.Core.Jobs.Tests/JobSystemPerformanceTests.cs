using System;
using System.Diagnostics;
using System.Threading;
using Xunit;

using Inno.Core.Jobs;


namespace Inno.Core.JobsSystem.Tests;

public sealed class JobSystemPerformanceTests
{
    [Fact]
    public void SameScheduleFlow_MoreWorkersIsFasterThanSingleWorker()
    {
        if (Environment.ProcessorCount < 2)
        {
            return;
        }

        const int jobCount = 64;
        const int sleepMilliseconds = 4;
        var multiWorkerCount = Math.Min(4, Environment.ProcessorCount);

        using var oneWorker = CreateWorkStealing(1);
        using var multiWorker = CreateWorkStealing(multiWorkerCount);

        _ = MeasureScheduleAndCompleteAll(oneWorker, 8, 1);
        _ = MeasureScheduleAndCompleteAll(multiWorker, 8, 1);

        var oneWorkerBest = MeasureBestOf(3, () => MeasureScheduleAndCompleteAll(oneWorker, jobCount, sleepMilliseconds));
        var multiWorkerBest = MeasureBestOf(3, () => MeasureScheduleAndCompleteAll(multiWorker, jobCount, sleepMilliseconds));

        Assert.True(
            multiWorkerBest < oneWorkerBest,
            $"Expected same schedule flow to be faster with more workers. oneWorker={oneWorkerBest.TotalMilliseconds:F2}ms, multiWorker={multiWorkerBest.TotalMilliseconds:F2}ms, workers={multiWorkerCount}");
    }

    [Fact]
    public void WorkStealingScheduleCompleteAll_IsFasterThanSingleThread()
    {
        if (Environment.ProcessorCount < 2)
        {
            return;
        }

        const int jobCount = 48;
        const int sleepMilliseconds = 4;
        var workerCount = Math.Min(4, Environment.ProcessorCount);

        using var single = new JobScheduler(JobExecutionMode.SingleThread);
        using var multi = CreateWorkStealing(workerCount);

        _ = MeasureScheduleAndCompleteAll(single, 8, 1);
        _ = MeasureScheduleAndCompleteAll(multi, 8, 1);

        var singleBest = MeasureBestOf(3, () => MeasureScheduleAndCompleteAll(single, jobCount, sleepMilliseconds));
        var multiBest = MeasureBestOf(3, () => MeasureScheduleAndCompleteAll(multi, jobCount, sleepMilliseconds));

        Assert.True(
            multiBest < singleBest,
            $"Expected work-stealing to be faster. single={singleBest.TotalMilliseconds:F2}ms, multi={multiBest.TotalMilliseconds:F2}ms, workers={workerCount}");
    }

    [Fact]
    public void WorkStealingParallelFor_IsFasterThanSingleThread()
    {
        if (Environment.ProcessorCount < 2)
        {
            return;
        }

        const int length = 48;
        const int batchSize = 1;
        const int sleepMilliseconds = 4;
        var workerCount = Math.Min(4, Environment.ProcessorCount);

        using var single = new JobScheduler(JobExecutionMode.SingleThread);
        using var multi = CreateWorkStealing(workerCount);

        _ = MeasureParallelFor(single, 8, 1, 1);
        _ = MeasureParallelFor(multi, 8, 1, 1);

        var singleBest = MeasureBestOf(3, () => MeasureParallelFor(single, length, batchSize, sleepMilliseconds));
        var multiBest = MeasureBestOf(3, () => MeasureParallelFor(multi, length, batchSize, sleepMilliseconds));

        Assert.True(
            multiBest < singleBest,
            $"Expected work-stealing parallel-for to be faster. single={singleBest.TotalMilliseconds:F2}ms, multi={multiBest.TotalMilliseconds:F2}ms, workers={workerCount}");
    }

    private static JobScheduler CreateWorkStealing(int workerCount)
    {
        return new JobScheduler(JobExecutionMode.WorkerPool, new JobSchedulerOptions
        {
            workerCount = workerCount
        });
    }

    private static TimeSpan MeasureBestOf(int runs, Func<TimeSpan> measure)
    {
        var best = TimeSpan.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var current = measure();
            if (current < best)
            {
                best = current;
            }
        }

        return best;
    }

    private static TimeSpan MeasureScheduleAndCompleteAll(JobScheduler jobs, int jobCount, int sleepMilliseconds)
    {
        jobs.BeginFrame();

        var handles = new JobHandle[jobCount];
        for (var i = 0; i < jobCount; i++)
        {
            handles[i] = jobs.Schedule(() => Thread.Sleep(sleepMilliseconds));
        }

        var stopwatch = Stopwatch.StartNew();
        jobs.CompleteAll(handles);
        stopwatch.Stop();

        jobs.EndFrame();
        return stopwatch.Elapsed;
    }

    private static TimeSpan MeasureParallelFor(JobScheduler jobs, int length, int batchSize, int sleepMilliseconds)
    {
        jobs.BeginFrame();

        var handle = jobs.ParallelFor(length, batchSize, (_, _) => Thread.Sleep(sleepMilliseconds));
        var stopwatch = Stopwatch.StartNew();
        jobs.Complete(handle);
        stopwatch.Stop();

        jobs.EndFrame();
        return stopwatch.Elapsed;
    }
}
