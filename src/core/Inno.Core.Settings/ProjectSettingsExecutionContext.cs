using System;
using System.Threading;

namespace Inno.Core.Settings;

/// <summary>
/// Binds one host-owned settings lookup to the current asynchronous script execution context.
/// </summary>
/// <remarks>
/// This type is a composition boundary for Editor and Player hosts. Game code should use
/// <see cref="Settings"/> instead of managing execution scopes directly.
/// </remarks>
public static class ProjectSettingsExecutionContext
{
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    /// <summary>
    /// Gets the settings lookup bound to the current asynchronous execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no settings lookup is active for the caller.
    /// </exception>
    public static IProjectSettingsLookup current
        => S_CURRENT_SCOPE.Value?.settings
            ?? throw new InvalidOperationException(
                "No project settings are bound to the current runtime execution context.");

    /// <summary>
    /// Binds a settings lookup until the returned strict last-in-first-out scope is disposed.
    /// </summary>
    /// <param name="settings">
    /// The host-owned settings lookup to expose to script-facing operations.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope owned by the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    public static IDisposable EnterScope(IProjectSettingsLookup settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var scope = new Scope(settings, S_CURRENT_SCOPE.Value);
        S_CURRENT_SCOPE.Value = scope;
        return scope;
    }

    private sealed class Scope(IProjectSettingsLookup settings, Scope? parent) : IDisposable
    {
        private bool m_disposed;

        internal IProjectSettingsLookup settings { get; } = settings;

        /// <summary>
        /// Restores the parent settings lookup after validating strict scope ordering.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a nested project settings scope is still active.
        /// </exception>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
            {
                throw new InvalidOperationException(
                    "Project settings scopes must be disposed in last-in-first-out order.");
            }
            m_disposed = true;
            S_CURRENT_SCOPE.Value = parent;
        }
    }
}
