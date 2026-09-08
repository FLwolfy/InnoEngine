using System;

namespace Inno.Audio;

/// <summary>
/// Marks a reloadable extension that creates the base audio mixer graph.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AudioMixerExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a mixer extension declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable mixer extension identifier.
    /// </param>
    public AudioMixerExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the globally stable mixer extension identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Marks a reloadable extension that contributes to an audio mixer graph.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AudioMixerFeatureExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a mixer feature declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable feature extension identifier.
    /// </param>
    /// <param name="priority">
    /// Feature invocation priority; lower values build first.
    /// </param>
    public AudioMixerFeatureExtensionAttribute(string id, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
        this.priority = priority;
    }

    /// <summary>
    /// Gets the globally stable feature extension identifier.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the feature invocation priority.
    /// </summary>
    public int priority { get; }
}

/// <summary>
/// Builds a base mixer graph on the host control thread.
/// </summary>
public abstract class AudioMixerExtension
{
    /// <summary>
    /// Adds base buses and processors to a candidate mixer graph.
    /// </summary>
    /// <param name="builder">
    /// Candidate graph builder valid only for this invocation.
    /// </param>
    /// <param name="state">
    /// Reload-safe neutral extension state.
    /// </param>
    public abstract void Build(AudioMixerBuilder builder, SerializedAudioExtensionState state);
}

/// <summary>
/// Contributes optional buses and processors to a candidate mixer graph.
/// </summary>
public abstract class AudioMixerFeature
{
    /// <summary>
    /// Adds feature-owned graph declarations on the host control thread.
    /// </summary>
    /// <param name="builder">
    /// Candidate graph builder valid only for this invocation.
    /// </param>
    /// <param name="state">
    /// Reload-safe neutral feature state.
    /// </param>
    public abstract void Build(AudioMixerBuilder builder, SerializedAudioExtensionState state);
}
