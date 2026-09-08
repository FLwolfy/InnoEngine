using System;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Inno.Core.Execution.Tests;

public sealed class ExecutionSlotTests
{
    [Fact]
    public void RetirementIsLifoAndRestoresParent()
    {
        var slot = new ExecutionSlot<object>("service");
        object first = new();
        object second = new();
        using IDisposable outer = slot.Enter(first);
        IDisposable inner = slot.Enter(second);
        Assert.Throws<InvalidOperationException>(outer.Dispose);
        Assert.Same(second, slot.current);
        inner.Dispose();
        Assert.Same(first, slot.current);
        inner.Dispose();
    }

    [Fact]
    public async Task RetiredBindingIsRevokedInInheritedAsyncContext()
    {
        var slot = new ExecutionSlot<object>("service");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable scope = slot.Enter(new object());
        Task<bool> inherited = Task.Run(async () =>
        {
            await release.Task;
            Assert.Throws<InvalidOperationException>(() => slot.current);
            return slot.TryGet(out _);
        });
        scope.Dispose();
        release.SetResult();
        Assert.False(await inherited);
        Assert.False(slot.TryGet(out _));
    }

    [Fact]
    public void SuspensionMasksAndRestoresParent()
    {
        var slot = new ExecutionSlot<object>("service");
        object value = new();
        using IDisposable outer = slot.Enter(value);
        using (slot.Suspend())
            Assert.False(slot.TryGet(out _));
        Assert.Same(value, slot.current);
    }

    [Fact]
    public void ThreadAffineBindingCannotBeReadOnWorker()
    {
        var slot = new ExecutionSlot<object>("owner");
        using IDisposable scope = slot.Enter(new object(), threadAffine: true);
        bool available = true;
        ExecutionContext context = ExecutionContext.Capture()!;
        var thread = new Thread(() => ExecutionContext.Run(context, _ => available = slot.TryGet(out _), null));
        thread.Start();
        thread.Join();
        Assert.False(available);
        Assert.True(slot.TryGet(out _));
    }

    [Fact]
    public async Task IndependentFlowsDoNotReplaceEachOther()
    {
        var slot = new ExecutionSlot<string>("flow");
        using IDisposable scope = slot.Enter("outer");
        await Task.Run(() =>
        {
            using IDisposable child = slot.Enter("child");
            Assert.Equal("child", slot.current);
        });
        Assert.Equal("outer", slot.current);
    }
}
