using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Scene;

namespace Inno.Editor.Scene;

internal static class SceneHistoryCompensation
{
    internal static SceneHistoryCompensationResult RemoveCreatedObjects(
        GameScene scene,
        IReadOnlySet<GameObject> existing,
        string description,
        EditorSceneWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        GameObject[] created = scene.GetObjects()
            .Where(gameObject => !existing.Contains(gameObject))
            .ToArray();
        var messages = new List<string>();
        bool statePreserved = true;
        for (int i = 0; i < created.Length; i++)
        {
            GameObject gameObject = created[i];
            SceneHistoryCompensationResult result = Remove(
                gameObject,
                () => scene.DestroyObject(gameObject),
                $"{description} GameObject '{gameObject.identity.persistentId}'",
                workspace);
            statePreserved &= result.statePreserved;
            if (!string.IsNullOrWhiteSpace(result.message))
                messages.Add(result.message);
        }

        GameObject[] survivors = scene.GetObjects()
            .Where(gameObject => !existing.Contains(gameObject))
            .ToArray();
        if (survivors.Length != 0)
        {
            statePreserved = false;
            messages.Add($"{description} left {survivors.Length} untracked GameObject(s) in the scene.");
        }
        return statePreserved
            ? SceneHistoryCompensationResult.Preserved(string.Join(" ", messages))
            : SceneHistoryCompensationResult.Lost(string.Join(" ", messages));
    }

    internal static SceneHistoryCompensationResult Remove(
        EngineObject target,
        Func<bool> remove,
        string description,
        EditorSceneWorkspace workspace)
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
            workspace.Find<EngineObject>(persistentId),
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
