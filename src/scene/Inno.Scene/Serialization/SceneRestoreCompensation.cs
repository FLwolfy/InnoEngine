using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;

using Inno.Core.Identity;
using Inno.Scene;

namespace Inno.Scene;

internal static class SceneRestoreCompensation
{
    internal static void RethrowAfterRemovingCreatedObjects(
        Exception restoreFailure,
        GameScene scene,
        IReadOnlySet<GameObject> existing,
        string description)
    {
        ArgumentNullException.ThrowIfNull(restoreFailure);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        GameObject[] created = scene.GetObjects()
            .Where(gameObject => !existing.Contains(gameObject))
            .ToArray();
        List<Exception>? cleanupFailures = null;
        for (int i = 0; i < created.Length; i++)
        {
            GameObject gameObject = created[i];
            if (!gameObject.isRuntimeValid)
                continue;
            try
            {
                _ = scene.DestroyObject(gameObject);
            }
            catch (Exception exception)
            {
                cleanupFailures ??= [];
                cleanupFailures.Add(exception);
            }
        }

        var survivorSet = new HashSet<GameObject>(
            created.Where(static gameObject => gameObject.isRuntimeValid),
            ReferenceEqualityComparer.Instance);
        survivorSet.UnionWith(scene.GetObjects().Where(gameObject => !existing.Contains(gameObject)));
        GameObject[] survivors = [.. survivorSet];
        for (int i = 0; i < created.Length; i++)
        {
            GameObject gameObject = created[i];
            if (!ReferenceEquals(
                    IdentityAllocator.current.Get<GameObject>(gameObject.identity.persistentId),
                    gameObject))
            {
                continue;
            }
            if (!survivors.Contains(gameObject, ReferenceEqualityComparer.Instance))
                survivors = [.. survivors, gameObject];
        }

        if (survivors.Length != 0)
        {
            cleanupFailures ??= [];
            cleanupFailures.Add(new InvalidOperationException(
                $"{description} left {survivors.Length} created GameObject(s) live or identity-registered."));
        }

        if (cleanupFailures is null)
            ExceptionDispatchInfo.Capture(restoreFailure).Throw();

        throw new InvalidOperationException(
            survivors.Length == 0
                ? $"{description} failed; created objects were removed, but one or more destruction callbacks also failed."
                : $"{description} failed and could not restore the exact prior scene state.",
            new AggregateException([restoreFailure, .. cleanupFailures]));
    }
}
