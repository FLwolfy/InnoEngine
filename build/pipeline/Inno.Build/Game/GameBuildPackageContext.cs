using System;

namespace Inno.Build;

/// <summary>
/// Supplies an isolated staging layout to a replaceable platform packager.
/// </summary>
public sealed class GameBuildPackageContext
{
    internal GameBuildPackageContext(
        BuildProfile profile,
        string supportPackDirectory,
        string contentDirectory,
        string outputDirectory)
    {
        this.profile = profile;
        this.supportPackDirectory = supportPackDirectory;
        this.contentDirectory = contentDirectory;
        this.outputDirectory = outputDirectory;
    }

    /// <summary>
    /// Gets the validated product profile.
    /// </summary>
    public BuildProfile profile { get; }

    /// <summary>
    /// Gets the verified read-only Support Pack directory for this target.
    /// </summary>
    public string supportPackDirectory { get; }

    /// <summary>
    /// Gets the source-free packaged content directory to deploy.
    /// </summary>
    public string contentDirectory { get; }

    /// <summary>
    /// Gets the empty staging parent where the target must create exactly one output.
    /// </summary>
    public string outputDirectory { get; }
}
