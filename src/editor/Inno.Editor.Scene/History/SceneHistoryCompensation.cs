using System;

using Inno.Core.Identity;
using Inno.Engine.Scene;

namespace Inno.Editor.Scene;

internal static class SceneHistoryCompensation
{
    internal static SceneHistoryCompensationResult Remove(
        EngineObject target,
        Func<bool> remove,
        string description)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Guid persistentId = target.identity.persistentId;
        Exception? failure = null;
        bool reportedRemoved = false;
        try
        {
            reportedRemoved = remove();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        bool remainsRegistered = ReferenceEquals(
            IdentityManager.Get<EngineObject>(persistentId),
            target);
        if (target.isDestroyed && !remainsRegistered)
        {
            return SceneHistoryCompensationResult.Preserved(
                failure is null
                    ? string.Empty
                    : $"{description} was removed, but its destruction callback failed: {failure.Message}");
        }

        if (failure is not null)
        {
            return SceneHistoryCompensationResult.Lost(
                $"{description} did not reach the destroyed and unregistered postcondition " +
                $"after removal threw: {failure.Message}");
        }
        return SceneHistoryCompensationResult.Lost(
            reportedRemoved
                ? $"{description} reported successful removal but did not reach the destroyed and unregistered postcondition."
                : $"{description} could not be fully destroyed and unregistered.");
    }
}

internal readonly record struct SceneHistoryCompensationResult(
    bool statePreserved,
    string message)
{
    internal static SceneHistoryCompensationResult Preserved(string message)
        => new(true, message);

    internal static SceneHistoryCompensationResult Lost(string message)
        => new(false, message);
}
