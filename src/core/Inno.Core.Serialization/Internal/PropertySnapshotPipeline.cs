using System;
using System.Collections.Generic;
using System.Linq;

namespace Inno.Core.Serialization;

internal static class PropertySnapshotPipeline
{
    internal static SerializationPropertySnapshot CaptureProperty(
        ISerializable value,
        string propertyName,
        SerializationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return Capture(value, context).FirstOrDefault(snapshot =>
                   string.Equals(snapshot.name, propertyName, StringComparison.Ordinal))
               ?? throw new ArgumentException(
                   $"Serializable property '{propertyName}' was not found on '{value.GetType().FullName}'.",
                   nameof(propertyName));
    }

    internal static IReadOnlyList<SerializationPropertySnapshot> Capture(
        ISerializable value,
        SerializationContext context)
    {
        SerializableMember[] members = ReflectionMetadata.GetSerializableMembers(value.GetType());
        var snapshots = new List<SerializationPropertySnapshot>(members.Length);
        for (int i = 0; i < members.Length; i++)
        {
            SerializableMember member = members[i];
            if ((member.visibility & PropertyVisibility.Serialize) == 0)
                continue;

            var operation = new SerializationOperation(context);
            try
            {
                SerializationNode node = ValuePipeline.Write(
                    member.GetValue(value),
                    member.type,
                    operation,
                    AppendPath(member.name),
                    allowDefaultObject: false);
                snapshots.Add(new SerializationPropertySnapshot(
                    member.name,
                    member.type,
                    BinarySerializationFormat.Encode(node)));
            }
            finally
            {
                operation.Fail();
            }
        }
        return snapshots;
    }

    internal static SerializationPropertyRestoreResult Restore(
        ISerializable target,
        IReadOnlyList<SerializationPropertySnapshot> snapshots,
        SerializationPropertyRestoreMode mode,
        SerializationContext context)
    {
        SerializableMember[] members = ReflectionMetadata.GetSerializableMembers(target.GetType());
        var membersByName = new Dictionary<string, SerializableMember>(members.Length, StringComparer.Ordinal);
        for (int i = 0; i < members.Length; i++)
        {
            SerializableMember member = members[i];
            if ((member.visibility & PropertyVisibility.Deserialize) != 0)
                membersByName.Add(member.name, member);
        }

        ValidateSnapshots(snapshots);
        var failures = new List<SerializationPropertyRestoreFailure>();
        int restoredCount = 0;
        int ignoredCount = 0;
        var operation = new SerializationOperation(context);
        try
        {
            for (int i = 0; i < snapshots.Count; i++)
            {
                SerializationPropertySnapshot snapshot = snapshots[i];
                if (!membersByName.TryGetValue(snapshot.name, out SerializableMember? member))
                {
                    ignoredCount++;
                    continue;
                }

                SerializationOperation.Checkpoint checkpoint = operation.CreateCheckpoint();
                try
                {
                    SerializationNode node = BinarySerializationFormat.Decode(snapshot.dataSpan);
                    object? value = ValuePipeline.Read(
                        node,
                        member.type,
                        operation,
                        AppendPath(member.name),
                        allowDefaultObject: false);
                    member.SetValue(target, value);
                    restoredCount++;
                }
                catch (Exception exception) when (
                    mode == SerializationPropertyRestoreMode.Compatible &&
                    IsRecoverable(exception))
                {
                    operation.Rollback(checkpoint);
                    failures.Add(new SerializationPropertyRestoreFailure(
                        member.name,
                        snapshot.propertyType,
                        member.type,
                        Unwrap(exception).Message));
                }
            }

            operation.ScheduleRestoredObject(target);
            operation.Complete();
            return new SerializationPropertyRestoreResult(restoredCount, ignoredCount, failures);
        }
        catch
        {
            operation.Fail();
            throw;
        }
    }

    private static void ValidateSnapshots(IReadOnlyList<SerializationPropertySnapshot> snapshots)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshots.Count; i++)
        {
            SerializationPropertySnapshot snapshot = snapshots[i]
                ?? throw new ArgumentException("A property snapshot collection cannot contain null entries.", nameof(snapshots));
            if (!names.Add(snapshot.name))
            {
                throw new ArgumentException(
                    $"Property snapshot key '{snapshot.name}' appears more than once.",
                    nameof(snapshots));
            }
        }
    }

    private static bool IsRecoverable(Exception exception)
        => exception is not OutOfMemoryException and
           not StackOverflowException and
           not AccessViolationException and
           not OperationCanceledException;

    private static Exception Unwrap(Exception exception)
        => exception is System.Reflection.TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException!
            : exception;

    private static string AppendPath(string name) => "$." + name;
}
