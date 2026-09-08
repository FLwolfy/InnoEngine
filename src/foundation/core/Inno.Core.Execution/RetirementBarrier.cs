using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Inno.Core.Execution;

/// <summary>
/// Bounds owner-thread retirement attempts without taking resource ownership or releasing pending dependencies.
/// </summary>
public sealed class RetirementBarrier
{
    private readonly string m_owner;
    private readonly TimeSpan m_timeout;
    private readonly int m_ownerThread = Environment.CurrentManagedThreadId;
    private Stopwatch? m_elapsed;
    private Exception? m_failure;
    private readonly List<Exception> m_completedFailures = [];
    private bool m_completed;

    /// <summary>
    /// Creates a deadline that starts with the first retirement attempt.
    /// </summary>
    /// <param name="owner">
    /// A diagnostic name identifying the retained resource owner.
    /// </param>
    /// <param name="timeout">
    /// The positive maximum drain duration; omitted values use thirty seconds.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The owner name is empty or the timeout is not positive.
    /// </exception>
    public RetirementBarrier(string owner, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        m_owner = owner;
        m_timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (m_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    /// <summary>
    /// Attempts retirement once at an owner-thread safe point.
    /// </summary>
    /// <param name="retire">
    /// The owner's retryable retirement operation; the barrier never retains this delegate.
    /// </param>
    /// <returns>
    /// True after retirement completes, or false while work remains within its deadline.
    /// </returns>
    /// <remarks>
    /// Wrapped pending failures retain their ownership semantics. A nested timeout is rethrown in its original
    /// exception tree. Ordinary sibling failures survive retries and are reported once retirement completes.
    /// </remarks>
    /// <exception cref="AggregateException">
    /// Retirement completed after earlier ordinary failures, or the owner reported a wrapped terminal timeout.
    /// </exception>
    /// <exception cref="RetirementTimeoutException">
    /// Work exceeded its deadline. The barrier remains terminally faulted and dependencies must be retained.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The caller is not the thread that created this barrier.
    /// </exception>
    public bool TryComplete(Action retire)
    {
        ArgumentNullException.ThrowIfNull(retire);
        if (m_ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Retirement must advance on its owner thread.");
        if (m_failure is not null)
            throw m_failure;
        if (m_completed)
            return true;
        m_elapsed ??= Stopwatch.StartNew();
        try
        {
            retire();
        }
        catch (Exception exception) when (RetirementPendingException.Find(exception) is not null)
        {
            RetirementPendingException.CollectCompletedFailures(exception, m_completedFailures);
            RetirementPendingException pending = RetirementPendingException.Find(exception)!;
            if (pending is RetirementTimeoutException)
            {
                m_failure = exception;
                throw;
            }
            if (m_elapsed.Elapsed < m_timeout)
                return false;
            m_failure = new RetirementTimeoutException(
                $"Owner '{m_owner}' could not retire within {m_timeout}: {pending.Message}",
                m_completedFailures.Count == 0 ? exception : new AggregateException(
                    "Retirement has unfinished work and earlier completed failures.", [.. m_completedFailures, exception]));
            throw m_failure;
        }
        catch (Exception exception)
        {
            m_completed = true;
            if (m_completedFailures.Count == 0)
                throw;
            RetirementPendingException.CollectCompletedFailures(exception, m_completedFailures);
            throw TakeCompletedFailure();
        }
        m_completed = true;
        if (m_completedFailures.Count > 0)
            throw TakeCompletedFailure();
        return true;
    }

    /// <summary>
    /// Drains a failed startup or final shutdown on the owner thread until completion or explicit failure.
    /// </summary>
    /// <param name="retire">
    /// The retryable operation, including any necessary owner-thread queue draining.
    /// </param>
    /// <exception cref="RetirementTimeoutException">
    /// Work exceeded its deadline; callers must fault admission and retain pending owners.
    /// </exception>
    public void Wait(Action retire)
    {
        while (!TryComplete(retire))
            Thread.Sleep(1);
    }

    private AggregateException TakeCompletedFailure()
    {
        var failure = new AggregateException($"Owner '{m_owner}' retired with completed cleanup failures.", m_completedFailures);
        m_completedFailures.Clear();
        return failure;
    }
}
