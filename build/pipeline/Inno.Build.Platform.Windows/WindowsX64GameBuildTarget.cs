using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Serialization;

namespace Inno.Build.Platform.Windows;

/// <summary>
/// Packages a verified Windows x64 Support Pack as a portable application directory.
/// </summary>
public sealed class WindowsX64GameBuildTarget : IGameBuildTarget
{
    private readonly BgfxGameContentCompiler m_contentCompiler;

    /// <summary>
    /// Creates a Windows target over one isolated authoring generation owner.
    /// </summary>
    /// <param name="assets">
    /// The authoring asset pipeline compiled for the Player.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns Shader IR contracts.
    /// </param>
    public WindowsX64GameBuildTarget(
        AssetPipeline assets,
        SerializationRegistry serialization)
    {
        m_contentCompiler = BgfxGameContentCompiler.CreateWindowsX64(assets, serialization);
    }

    /// <summary>
    /// Gets the Windows x64 target identity.
    /// </summary>
    public BuildTargetId id => BuildTargetId.windowsX64;

    /// <summary>
    /// Compiles every supported Windows shader backend and portable texture artifact.
    /// </summary>
    /// <param name="context">
    /// The isolated target-content staging context.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels offline compilation.
    /// </param>
    /// <returns>
    /// An operation that completes when all target artifacts are staged.
    /// </returns>
    public ValueTask BuildContentAsync(
        GameBuildContentContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return m_contentCompiler.CompileAsync(context, cancellationToken);
    }

    /// <summary>
    /// Composes a Windows application directory in isolated staging.
    /// </summary>
    /// <param name="context">
    /// The verified Support Pack, packaged content, and output staging paths.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels composition before commit.
    /// </param>
    /// <returns>
    /// The staged Windows application directory.
    /// </returns>
    public async ValueTask<string> PackageAsync(
        GameBuildPackageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        string application = Path.Combine(context.outputDirectory, context.profile.productName + "-Windows-x64");
        await CopyDirectoryAsync(context.supportPackDirectory, application, cancellationToken).ConfigureAwait(false);
        string player = Path.Combine(application, "Inno.Player.exe");
        if (!File.Exists(player))
            throw new InvalidDataException("The Windows Support Pack does not contain Inno.Player.exe.");
        File.Move(player, Path.Combine(application, context.profile.productName + ".exe"));
        await CopyDirectoryAsync(context.contentDirectory, Path.Combine(application, "Content"), cancellationToken)
            .ConfigureAwait(false);
        return application;
    }

    private static async ValueTask CopyDirectoryAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
            await using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }
}
