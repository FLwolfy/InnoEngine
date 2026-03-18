using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

using Inno.Core.Coroutines;

using Xunit;

namespace Inno.Core.Coroutines.Tests;

public sealed class CoroutineSchedulerTests
{
    [Fact]
    public void StartCoroutine_NullYield_RunsOnNextTick()
    {
        using var scheduler = new CoroutineScheduler();
        var ran = false;

        scheduler.StartCoroutine(Routine());
        scheduler.Tick(0.016f);
        Assert.False(ran);
        scheduler.Tick(0.016f);
        Assert.True(ran);
        return;

        IEnumerator Routine()
        {
            yield return null;
            ran = true;
        }
    }

    [Fact]
    public void WaitForSeconds_WaitsByTime()
    {
        using var scheduler = new CoroutineScheduler();
        var ran = false;

        scheduler.StartCoroutine(Routine());
        scheduler.Tick(0.3f);
        Assert.False(ran);
        scheduler.Tick(0.3f);
        Assert.False(ran);
        scheduler.Tick(0.4f);
        Assert.False(ran);
        scheduler.Tick(0.4f);
        Assert.True(ran);
        return;

        IEnumerator Routine()
        {
            yield return new WaitForSeconds(1.0f);
            ran = true;
        }
    }

    [Fact]
    public void WaitUntil_And_WaitWhile_Work()
    {
        using var scheduler = new CoroutineScheduler();
        var flag = false;
        var done = false;

        scheduler.StartCoroutine(Routine());
        scheduler.Tick(0.016f);
        Assert.False(done);

        flag = true;
        scheduler.Tick(0.016f);
        Assert.False(done);

        flag = false;
        scheduler.Tick(0.016f);
        Assert.True(done);
        return;

        IEnumerator Routine()
        {
            yield return new WaitUntil(() => flag);
            yield return new WaitWhile(() => flag);
            done = true;
        }
    }

    [Fact]
    public void StopCoroutine_StopsTarget()
    {
        using var scheduler = new CoroutineScheduler();
        var count = 0;
        CoroutineHandle handle = scheduler.StartCoroutine(Routine());

        scheduler.Tick(0.016f);
        Assert.Equal(1, count);
        Assert.True(scheduler.StopCoroutine(handle));
        scheduler.Tick(0.016f);
        Assert.Equal(1, count);
        return;

        IEnumerator Routine()
        {
            while (true)
            {
                count++;
                yield return null;
            }
        }
    }

    [Fact]
    public void StopAllCoroutines_ByOwner_StopsOnlyThatOwner()
    {
        using var scheduler = new CoroutineScheduler();
        var ownerA = new object();
        var ownerB = new object();
        var a = 0;
        var b = 0;

        scheduler.StartCoroutine(ownerA, Loop(() => a++));
        scheduler.StartCoroutine(ownerB, Loop(() => b++));
        scheduler.Tick(0.016f);
        Assert.Equal(1, a);
        Assert.Equal(1, b);

        scheduler.StopAllCoroutines(ownerA);
        scheduler.Tick(0.016f);
        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void NestedCoroutine_Works()
    {
        using var scheduler = new CoroutineScheduler();
        var order = "";

        scheduler.StartCoroutine(Routine());
        scheduler.Tick(0.016f);
        scheduler.Tick(0.016f);
        scheduler.Tick(0.016f);

        Assert.Equal("ABC", order);
        return;

        IEnumerator Routine()
        {
            order += "A";
            yield return Child();
            order += "C";
        }

        IEnumerator Child()
        {
            yield return null;
            order += "B";
        }
    }

    [Fact]
    public void WaitForTask_WaitsCompletion()
    {
        using var scheduler = new CoroutineScheduler();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;

        scheduler.StartCoroutine(Routine());
        scheduler.Tick(0.016f);
        Assert.False(completed);

        tcs.SetResult(true);
        scheduler.Tick(0.016f);
        Assert.True(completed);
        return;

        IEnumerator Routine()
        {
            yield return new WaitForTask(tcs.Task);
            completed = true;
        }
    }

    [Fact]
    public void ListenOnceStyle_YieldCoroutineHandle_WaitsTarget()
    {
        using var scheduler = new CoroutineScheduler();
        var done = false;

        CoroutineHandle child = scheduler.StartCoroutine(Child());
        scheduler.StartCoroutine(Parent(child));
        scheduler.Tick(0.016f);
        Assert.False(done);
        scheduler.Tick(0.016f);
        Assert.True(done);
        return;

        IEnumerator Child()
        {
            yield return null;
        }

        IEnumerator Parent(CoroutineHandle handle)
        {
            yield return handle;
            done = true;
        }
    }

    [Fact]
    public void ConcurrentStartAndTick_IsStable()
    {
        using var scheduler = new CoroutineScheduler();
        const int workers = 8;
        const int perWorker = 4000;
        var count = 0;

        Parallel.For(0, workers, _ =>
        {
            for (int i = 0; i < perWorker; i++)
            {
                scheduler.StartCoroutine(Routine());
            }
        });

        scheduler.Tick(0.016f);
        scheduler.Tick(0.016f);

        Assert.Equal(workers * perWorker, count);
        return;

        IEnumerator Routine()
        {
            yield return null;
            Interlocked.Increment(ref count);
        }
    }

    [Fact]
    public void ConcurrentStopByHandle_IsStable()
    {
        using var scheduler = new CoroutineScheduler();
        const int count = 3000;
        CoroutineHandle[] handles = new CoroutineHandle[count];
        var ran = 0;

        for (int i = 0; i < count; i++)
        {
            handles[i] = scheduler.StartCoroutine(Routine());
        }

        Parallel.For(0, count, i => { scheduler.StopCoroutine(handles[i]); });
        scheduler.Tick(0.016f);
        scheduler.Tick(0.016f);

        Assert.Equal(0, ran);
        return;

        IEnumerator Routine()
        {
            yield return null;
            Interlocked.Increment(ref ran);
        }
    }

    [Fact]
    public void HandleIsValid_TracksLifecycle()
    {
        using var scheduler = new CoroutineScheduler();
        CoroutineHandle handle = scheduler.StartCoroutine(Routine());

        Assert.True(handle.isValid);
        scheduler.Tick(0.016f);
        Assert.True(handle.isValid);
        scheduler.Tick(0.016f);
        Assert.False(handle.isValid);
        return;

        IEnumerator Routine()
        {
            yield return null;
        }
    }

    [Fact]
    public void HandleIsValid_BecomesFalseAfterStop()
    {
        using var scheduler = new CoroutineScheduler();
        CoroutineHandle handle = scheduler.StartCoroutine(Loop(() => { }));

        Assert.True(handle.isValid);
        Assert.True(scheduler.StopCoroutine(handle));
        scheduler.Tick(0.016f);
        Assert.False(handle.isValid);
    }

    [Fact]
    public void StopCoroutine_FromOtherScheduler_ReturnsFalseAndDoesNotAffectOwner()
    {
        using var schedulerA = new CoroutineScheduler();
        using var schedulerB = new CoroutineScheduler();
        var count = 0;

        CoroutineHandle handleA = schedulerA.StartCoroutine(Loop(() => Interlocked.Increment(ref count)));
        schedulerA.Tick(0.016f);
        Assert.Equal(1, count);

        Assert.False(schedulerB.StopCoroutine(handleA));
        schedulerA.Tick(0.016f);
        Assert.Equal(2, count);
    }

    [Fact]
    public void StartCoroutine_AfterDispose_ThrowsAndHandleCannotBeCreated()
    {
        using var scheduler = new CoroutineScheduler();
        scheduler.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scheduler.StartCoroutine(Loop(() => { })));
    }

    private static IEnumerator Loop(Action onTick)
    {
        while (true)
        {
            onTick.Invoke();
            yield return null;
        }
    }
}
