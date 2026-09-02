using System.Threading;
using System.Threading.Tasks;

namespace Inno.Build;

/// <summary>
/// Defines the replaceable platform packaging boundary for one game build target.
/// </summary>
public interface IGameBuildTarget
{
    /// <summary>
    /// Gets the stable target identity implemented by this packager.
    /// </summary>
    BuildTargetId id { get; }

    /// <summary>
    /// Produces every target-specific runtime artifact required by this platform.
    /// </summary>
    /// <param name="context">
    /// The isolated target-content staging context.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels offline compilation before package commit.
    /// </param>
    /// <returns>
    /// An operation that completes after all target artifacts are durably staged.
    /// </returns>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown when target artifact generation is canceled.
    /// </exception>
    ValueTask BuildContentAsync(
        GameBuildContentContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes one platform output from a verified Support Pack and source-free content directory.
    /// </summary>
    /// <param name="context">
    /// The isolated package context owned by the current build.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels work before the build is committed.
    /// </param>
    /// <returns>
    /// The single platform output path created beneath <see cref="GameBuildPackageContext.outputDirectory"/>.
    /// </returns>
    /// <exception cref="System.OperationCanceledException">
    /// Thrown when packaging is canceled.
    /// </exception>
    ValueTask<string> PackageAsync(
        GameBuildPackageContext context,
        CancellationToken cancellationToken = default);
}
