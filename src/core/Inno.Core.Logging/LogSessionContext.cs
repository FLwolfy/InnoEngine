using System;
using System.Threading;

namespace Inno.Core.Logging;

/// <summary>
/// Associates logs written on one asynchronous execution flow with an isolated runtime session.
/// </summary>
public static class LogSessionContext
{
    private sealed class Scope(LogSessionId sessionId, Scope? parent) : IDisposable
    {
        private bool m_disposed;

        internal LogSessionId sessionId { get; } = sessionId;

        internal Scope? parent { get; } = parent;

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT.Value, this))
            {
                throw new InvalidOperationException(
                    "Log session scopes must be disposed in reverse creation order on their owning execution flow.");
            }
            S_CURRENT.Value = parent;
            m_disposed = true;
        }
    }

    private static readonly AsyncLocal<Scope?> S_CURRENT = new();

    /// <summary>
    /// Gets the runtime session associated with the current asynchronous execution flow.
    /// </summary>
    public static LogSessionId current => S_CURRENT.Value?.sessionId ?? LogSessionId.none;

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
        var scope = new Scope(sessionId, S_CURRENT.Value);
        S_CURRENT.Value = scope;
        return scope;
    }
}
