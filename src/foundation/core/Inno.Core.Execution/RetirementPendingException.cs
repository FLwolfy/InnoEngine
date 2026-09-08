using System;
using System.Collections.Generic;

namespace Inno.Core.Execution;

/// <summary>
/// Reports that cancellation has begun but the owner must retain its dependencies until work drains.
/// </summary>
public class RetirementPendingException : InvalidOperationException
{
    /// <summary>
    /// Creates a retryable retirement barrier for an owner with unfinished work.
    /// </summary>
    /// <param name="message">
    /// The owner and work preventing synchronous release.
    /// </param>
    /// <param name="innerException">
    /// The original retirement failure, including any contextual wrappers, or null when there is no cause.
    /// </param>
    public RetirementPendingException(string message, Exception? innerException = null) : base(message, innerException) { }

    /// <summary>
    /// Finds unfinished retirement in an exception tree without discarding its contextual or sibling failures.
    /// </summary>
    /// <param name="exception">
    /// The exception to classify, including aggregate and ordinary inner-exception wrappers.
    /// </param>
    /// <returns>
    /// A nested timeout when present, otherwise the first pending signal, or null when retirement is not pending.
    /// Callers must propagate and retain the original exception rather than replacing it with this classification.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// The exception is null.
    /// </exception>
    public static RetirementPendingException? Find(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is RetirementPendingException pending)
            return pending.InnerException is Exception detail && Find(detail) is RetirementTimeoutException terminal
                ? terminal : pending;
        if (exception is not AggregateException aggregate)
            return exception.InnerException is Exception cause ? Find(cause) : null;
        RetirementPendingException? result = null;
        foreach (Exception inner in aggregate.InnerExceptions)
        {
            RetirementPendingException? found = Find(inner);
            if (found is RetirementTimeoutException)
                return found;
            result ??= found;
        }
        return result;
    }

    /// <summary>
    /// Retains ordinary failure branches across pending retries without recording transient pending signals as completed work.
    /// </summary>
    /// <param name="exception">
    /// The original exception tree reported by a retirement attempt.
    /// </param>
    /// <param name="failures">
    /// The owner's failure collection; existing exception instances are not added twice.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// The exception or collection is null.
    /// </exception>
    public static void CollectCompletedFailures(Exception exception, ICollection<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(failures);
        if (exception is RetirementPendingException pending)
        {
            if (pending.InnerException is Exception detail)
                CollectCompletedFailures(detail, failures);
            return;
        }
        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
                CollectCompletedFailures(inner, failures);
            return;
        }
        if (Find(exception) is not null && exception.InnerException is Exception cause)
        {
            CollectCompletedFailures(cause, failures);
            return;
        }
        foreach (Exception previous in failures)
            if (ReferenceEquals(previous, exception)) return;
        failures.Add(exception);
    }
}
