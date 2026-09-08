using System;
using System.IO;

using Inno.Core.IO;
using Inno.Core.Serialization;

namespace Inno.Build;

/// <summary>
/// Persists one project's current game build profile through the engine serialization contract.
/// </summary>
/// <remarks>
/// The store writes a complete candidate beside the destination and replaces the active document
/// only after serialization and validation succeed.
/// </remarks>
public sealed class BuildProfileStore
{
    private readonly string m_path;
    private readonly SerializationRegistry m_serialization;

    /// <summary>
    /// Creates a store for one current-format build profile document.
    /// </summary>
    /// <param name="path">
    /// The project-owned path of the build profile document.
    /// </param>
    /// <param name="serialization">
    /// The serialization registry whose active generation owns the generated converter.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="path"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serialization"/> is <see langword="null"/>.
    /// </exception>
    public BuildProfileStore(string path, SerializationRegistry serialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(serialization);
        m_path = Path.GetFullPath(path);
        m_serialization = serialization;
    }

    /// <summary>
    /// Gets whether the current project contains a build profile document.
    /// </summary>
    public bool exists => File.Exists(m_path);

    /// <summary>
    /// Loads and validates the current build profile document.
    /// </summary>
    /// <returns>
    /// A newly owned build profile that callers may edit independently.
    /// </returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the project does not contain a build profile document.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the document is corrupt or contains an invalid profile.
    /// </exception>
    public BuildProfile Load()
    {
        if (!File.Exists(m_path))
            throw new FileNotFoundException("The project build profile does not exist.", m_path);

        BuildProfile profile;
        try
        {
            profile = m_serialization.Deserialize<BuildProfile>(File.ReadAllBytes(m_path));
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            throw new InvalidDataException($"Build profile '{m_path}' is not a valid current-format document.", exception);
        }
        profile.Validate();
        return profile;
    }

    /// <summary>
    /// Validates and atomically saves the supplied build profile.
    /// </summary>
    /// <param name="profile">
    /// The complete current profile to persist.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the profile cannot produce a supported Player deployment.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the candidate document cannot be written or atomically installed.
    /// </exception>
    public void Save(BuildProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        AtomicFile.WriteAllBytes(m_path, m_serialization.Serialize(profile));
    }
}
