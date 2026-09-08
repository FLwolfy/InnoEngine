using Inno.Core.Execution;
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
    private static readonly ExecutionSlot<IProjectSettingsLookup> S_CURRENT_SCOPE = new("settings");

    /// <summary>
    /// Gets the settings lookup bound to the current asynchronous execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no settings lookup is active for the caller.
    /// </exception>
    public static IProjectSettingsLookup current
        => S_CURRENT_SCOPE.current;

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
        return S_CURRENT_SCOPE.Enter(settings);
    }

}
