using System;
using System.Reflection;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class PlatformImGuiContextTests
{
    [Fact]
    public void LiveResizeHoverLockDefersOnlyTheMatchingPendingMouseLeave()
    {
        Type contextType = Assembly.Load("Inno.Platform.ImGui").GetType(
            "Inno.Platform.ImGui.PlatformImGuiContext",
            throwOnError: true)!;
        MethodInfo shouldFlush = contextType.GetMethod(
            "ShouldFlushPendingMouseLeave",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.False(InvokeShouldFlush(
            shouldFlush,
            pendingFrame: 10,
            currentFrame: 10,
            mouseButtonsDown: 0,
            pendingWindowId: 7,
            liveResizeLockedWindowId: 7));
        Assert.True(InvokeShouldFlush(
            shouldFlush,
            pendingFrame: 10,
            currentFrame: 10,
            mouseButtonsDown: 0,
            pendingWindowId: 7,
            liveResizeLockedWindowId: 0));
        Assert.True(InvokeShouldFlush(
            shouldFlush,
            pendingFrame: 10,
            currentFrame: 10,
            mouseButtonsDown: 0,
            pendingWindowId: 7,
            liveResizeLockedWindowId: 8));
        Assert.False(InvokeShouldFlush(
            shouldFlush,
            pendingFrame: 11,
            currentFrame: 10,
            mouseButtonsDown: 0,
            pendingWindowId: 7,
            liveResizeLockedWindowId: 0));
    }

    private static bool InvokeShouldFlush(
        MethodInfo method,
        int pendingFrame,
        int currentFrame,
        int mouseButtonsDown,
        uint pendingWindowId,
        uint liveResizeLockedWindowId)
        => (bool)method.Invoke(
            null,
            [
                pendingFrame,
                currentFrame,
                mouseButtonsDown,
                pendingWindowId,
                liveResizeLockedWindowId
            ])!;
}
