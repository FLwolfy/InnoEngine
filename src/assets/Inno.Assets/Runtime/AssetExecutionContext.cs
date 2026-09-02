using System;
using System.Threading;

namespace Inno.Assets;

/// <summary>
/// Binds one host-owned asset lookup to the current asynchronous script execution context.
/// </summary>
/// <remarks>
/// This type is a composition boundary for Editor and Player hosts. Game code should use
/// <see cref="Assets"/> instead of managing execution scopes directly.
/// </remarks>
public static class AssetExecutionContext
{
    private static readonly AsyncLocal<Scope?> S_CURRENT_SCOPE = new();

    /// <summary>
    /// Gets the asset lookup bound to the current asynchronous execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active for the caller.
    /// </exception>
    public static IAssetLookup current
        => S_CURRENT_SCOPE.Value?.assets
            ?? throw new InvalidOperationException(
                "No asset lookup is bound to the current runtime execution context.");

    /// <summary>
    /// Binds an asset lookup until the returned strict last-in-first-out scope is disposed.
    /// </summary>
    /// <param name="assets">
    /// The host-owned lookup to expose to script-facing asset operations.
    /// </param>
    /// <returns>
    /// A strict last-in-first-out execution scope owned by the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="assets"/> is <see langword="null"/>.
    /// </exception>
    public static IDisposable EnterScope(IAssetLookup assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var scope = new Scope(assets, S_CURRENT_SCOPE.Value);
        S_CURRENT_SCOPE.Value = scope;
        return scope;
    }

    private sealed class Scope(IAssetLookup assets, Scope? parent) : IDisposable
    {
        private bool m_disposed;

        internal IAssetLookup assets { get; } = assets;

        /// <summary>
        /// Restores the parent asset lookup after validating strict scope ordering.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a nested asset execution scope is still active.
        /// </exception>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT_SCOPE.Value, this))
            {
                throw new InvalidOperationException(
                    "Asset execution scopes must be disposed in last-in-first-out order.");
            }
            m_disposed = true;
            S_CURRENT_SCOPE.Value = parent;
        }
    }
}
