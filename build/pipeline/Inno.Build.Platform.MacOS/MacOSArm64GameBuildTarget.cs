using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Serialization;

namespace Inno.Build.Platform.MacOS;

/// <summary>
/// Packages a verified macOS ARM64 Support Pack as a native application bundle.
/// </summary>
public sealed class MacOSArm64GameBuildTarget : IGameBuildTarget
{
    private readonly BgfxGameContentCompiler m_contentCompiler;

    /// <summary>
    /// Creates a macOS target over one isolated authoring generation owner.
    /// </summary>
    /// <param name="assets">
    /// The authoring asset pipeline compiled for the Player.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry that owns Shader IR contracts.
    /// </param>
    public MacOSArm64GameBuildTarget(
        AssetPipeline assets,
        SerializationRegistry serialization)
    {
        m_contentCompiler = BgfxGameContentCompiler.CreateMacOSArm64(assets, serialization);
    }

    /// <summary>
    /// Gets the macOS ARM64 target identity.
    /// </summary>
    public BuildTargetId id => BuildTargetId.macOSArm64;

    /// <summary>
    /// Compiles Metal shader variants and portable texture artifacts for the macOS Player.
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
    /// Composes a macOS application bundle in isolated staging.
    /// </summary>
    /// <param name="context">
    /// The verified Support Pack, packaged content, and output staging paths.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels composition before commit.
    /// </param>
    /// <returns>
    /// The staged application bundle path.
    /// </returns>
    public async ValueTask<string> PackageAsync(
        GameBuildPackageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        string application = Path.Combine(context.outputDirectory, context.profile.productName + ".app");
        string contents = Path.Combine(application, "Contents");
        string executableRoot = Path.Combine(contents, "MacOS");
        string resources = Path.Combine(contents, "Resources");
        await CopyDirectoryAsync(context.supportPackDirectory, executableRoot, cancellationToken)
            .ConfigureAwait(false);
        string player = Path.Combine(executableRoot, "Inno.Player");
        if (!File.Exists(player))
            throw new InvalidDataException("The macOS Support Pack does not contain Inno.Player.");
        File.Move(player, Path.Combine(executableRoot, context.profile.productName));
        await CopyDirectoryAsync(context.contentDirectory, Path.Combine(resources, "Content"), cancellationToken)
            .ConfigureAwait(false);
        Directory.CreateDirectory(contents);
        string product = SecurityElement.Escape(context.profile.productName) ?? context.profile.productName;
        string identifier = SecurityElement.Escape(context.profile.applicationId) ?? context.profile.applicationId;
        await File.WriteAllTextAsync(
                Path.Combine(contents, "Info.plist"),
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0"><dict>
                  <key>CFBundleExecutable</key><string>{product}</string>
                  <key>CFBundleIdentifier</key><string>{identifier}</string>
                  <key>CFBundleName</key><string>{product}</string>
                  <key>CFBundlePackageType</key><string>APPL</string>
                  <key>NSHighResolutionCapable</key><true/>
                </dict></plist>
                """,
                cancellationToken)
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
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, File.GetUnixFileMode(file));
        }
    }
}
