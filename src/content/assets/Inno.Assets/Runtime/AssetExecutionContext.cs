using Inno.Core.Execution;
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
    private static readonly ExecutionSlot<IAssetLookup> S_CURRENT_SCOPE = new("assets");

    /// <summary>
    /// Gets the asset lookup bound to the current asynchronous execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no asset lookup is active for the caller.
    /// </exception>
    public static IAssetLookup current
        => S_CURRENT_SCOPE.current;

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
        return S_CURRENT_SCOPE.Enter(assets);
    }

}
