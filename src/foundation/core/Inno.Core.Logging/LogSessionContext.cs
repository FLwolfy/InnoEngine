using Inno.Core.Execution;
using System;
using System.Threading;

namespace Inno.Core.Logging;

/// <summary>
/// Associates logs written on one asynchronous execution flow with an isolated runtime session.
/// </summary>
public static class LogSessionContext
{

    private static readonly ExecutionSlot<LogSessionId> S_CURRENT = new("sessionId");

    /// <summary>
    /// Gets the runtime session associated with the current asynchronous execution flow.
    /// </summary>
    public static LogSessionId current => S_CURRENT.TryGet(out LogSessionId session) ? session : LogSessionId.none;

    /// <summary>
    /// Enters a nested execution scope whose emitted logs belong to the supplied runtime session.
    /// </summary>
    /// <param name="sessionId">
    /// The non-empty identifier of the runtime session receiving execution.
    /// </param>
    /// <returns>
    /// A scope that restores the previous session when disposed in reverse creation order.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sessionId"/> does not identify a runtime session.
    /// </exception>
    public static IDisposable Enter(LogSessionId sessionId)
    {
        if (!sessionId.isAssigned)
            throw new ArgumentException("A runtime log session identifier is required.", nameof(sessionId));
        return S_CURRENT.Enter(sessionId);
    }
}
