using System;
using Inno.Core.Serialization;

namespace Inno.Build;

/// <summary>
/// Supplies isolated staging and profile data for target-specific offline artifact generation.
/// </summary>
public sealed class GameBuildContentContext
{
    internal GameBuildContentContext(
        BuildProfile profile,
        string outputDirectory,
        SerializationGeneration serialization)
    {
        this.profile = profile;
        this.outputDirectory = outputDirectory;
        this.serialization = serialization;
    }

    /// <summary>
    /// Gets the validated product and target profile.
    /// </summary>
    public BuildProfile profile { get; }

    /// <summary>
    /// Gets the empty staging directory that receives source-free target artifacts.
    /// </summary>
    public string outputDirectory { get; }

    /// <summary>
    /// Gets the immutable converter generation pinned by the surrounding build transaction.
    /// </summary>
    public SerializationGeneration serialization { get; }
}
