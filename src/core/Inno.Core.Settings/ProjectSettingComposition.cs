using System;
using System.Collections.Generic;

using Inno.Core.Serialization;

namespace Inno.Core.Settings;

/// <summary>
/// Declares the protocol-owned composer used to combine contributions for one project setting.
/// Settings without a composer retain the default whole-value replacement behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProjectSettingComposerAttribute : Attribute
{
    /// <summary>
    /// Creates a composer declaration for one stable setting protocol.
    /// </summary>
    /// <param name="settingId">
    /// The setting protocol composed by the attributed type.
    /// </param>
    public ProjectSettingComposerAttribute(string settingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingId);
        this.settingId = new ProjectSettingId(settingId);
    }

    /// <summary>
    /// Gets the stable setting protocol composed by the attributed type.
    /// </summary>
    public ProjectSettingId settingId { get; }
}

/// <summary>
/// Identifies where one setting contribution originated.
/// </summary>
public enum ProjectSettingContributionSource
{
    /// <summary>
    /// The contribution is a default supplied by an activated Plugin.
    /// </summary>
    Plugin,

    /// <summary>
    /// The contribution is the project-authored delta with highest precedence.
    /// </summary>
    Project
}

/// <summary>
/// Exposes ownership and dependency information while a protocol-owned composer combines one contribution.
/// </summary>
public sealed class ProjectSettingContributionContext
{
    private readonly IReadOnlySet<string> m_dependencies;
    private readonly IReadOnlySet<string> m_overrides;

    internal ProjectSettingContributionContext(
        string contributorId,
        ProjectSettingContributionSource source,
        IReadOnlySet<string> dependencies,
        IReadOnlySet<string> overrides)
    {
        this.contributorId = contributorId;
        this.source = source;
        m_dependencies = dependencies;
        m_overrides = overrides;
    }

    /// <summary>
    /// Gets the stable Plugin ID, or <c>project</c> for the project-authored contribution.
    /// </summary>
    public string contributorId { get; }

    /// <summary>
    /// Gets the contribution source.
    /// </summary>
    public ProjectSettingContributionSource source { get; }

    /// <summary>
    /// Gets whether this is the project-authored highest-precedence contribution.
    /// </summary>
    public bool isProject => source == ProjectSettingContributionSource.Project;

    /// <summary>
    /// Gets whether this contribution may explicitly replace data owned by another contributor.
    /// </summary>
    /// <param name="ownerId">
    /// The stable owner whose data would be replaced.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for project data, or when the Plugin both depends on and explicitly overrides
    /// <paramref name="ownerId"/>.
    /// </returns>
    public bool CanOverride(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        return isProject
               || string.Equals(ownerId, "host", StringComparison.Ordinal)
               || (m_dependencies.Contains(ownerId) && m_overrides.Contains(ownerId));
    }
}

/// <summary>
/// Provides one decoded contribution and its immutable composition context.
/// </summary>
/// <typeparam name="TContribution">
/// The protocol-owned neutral contribution type.
/// </typeparam>
public sealed class ProjectSettingContribution<TContribution>
    where TContribution : class, ISerializable
{
    internal ProjectSettingContribution(
        ProjectSettingContributionContext context,
        TContribution value)
    {
        this.context = context;
        this.value = value;
    }

    /// <summary>
    /// Gets the ownership and dependency context for this contribution.
    /// </summary>
    public ProjectSettingContributionContext context { get; }

    /// <summary>
    /// Gets the decoded protocol-owned contribution value.
    /// </summary>
    public TContribution value { get; }
}

/// <summary>
/// Defines the non-generic base for a protocol-owned project setting composer.
/// </summary>
public abstract class ProjectSettingComposer
{
    internal abstract Type settingType { get; }

    internal abstract bool TryCapture(
        SerializationRegistry serialization,
        ISerializable baseline,
        ISerializable value,
        out byte[] contributionData);

    internal abstract ISerializable Compose(
        SerializationRegistry serialization,
        ISerializable hostDefault,
        IReadOnlyList<ProjectSettingCompositionEntry> contributions);
}

/// <summary>
/// Lets one setting protocol define deterministic delta capture and multi-contributor composition.
/// </summary>
/// <typeparam name="TSetting">
/// The exact effective setting type.
/// </typeparam>
/// <typeparam name="TContribution">
/// The exact serializable delta type stored in Plugin and project records.
/// </typeparam>
public abstract class ProjectSettingComposer<TSetting, TContribution> : ProjectSettingComposer
    where TSetting : class, ISerializable
    where TContribution : class, ISerializable
{
    /// <summary>
    /// Captures the contribution introduced by the supplied project setting value.
    /// </summary>
    /// <param name="baseline">
    /// The value composed from lower-precedence contributors.
    /// </param>
    /// <param name="value">
    /// The complete authored value.
    /// </param>
    /// <returns>
    /// A detached neutral contribution.
    /// </returns>
    protected abstract TContribution CaptureContribution(TSetting baseline, TSetting value);

    /// <summary>
    /// Gets whether a captured contribution contains no semantic operation.
    /// </summary>
    /// <param name="contribution">
    /// The contribution to inspect.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the record should be omitted.
    /// </returns>
    protected abstract bool IsEmpty(TContribution contribution);

    /// <summary>
    /// Composes dependency-ordered Plugin deltas and the optional final project delta into a host default.
    /// </summary>
    /// <param name="target">
    /// The newly created host default to mutate into the effective value.
    /// </param>
    /// <param name="contributions">
    /// Decoded contributions in deterministic precedence order.
    /// </param>
    protected abstract void Compose(
        TSetting target,
        IReadOnlyList<ProjectSettingContribution<TContribution>> contributions);

    internal sealed override Type settingType => typeof(TSetting);

    internal sealed override bool TryCapture(
        SerializationRegistry serialization,
        ISerializable baseline,
        ISerializable value,
        out byte[] contributionData)
    {
        if (baseline is not TSetting typedBaseline || value is not TSetting typedValue)
        {
            throw new ArgumentException(
                $"Composer '{GetType().FullName}' requires setting type '{typeof(TSetting).FullName}'.");
        }
        TContribution contribution = CaptureContribution(typedBaseline, typedValue)
            ?? throw new InvalidOperationException(
                $"Composer '{GetType().FullName}' returned a null contribution.");
        if (IsEmpty(contribution))
        {
            contributionData = [];
            return false;
        }
        contributionData = serialization.Serialize(contribution);
        return true;
    }

    internal sealed override ISerializable Compose(
        SerializationRegistry serialization,
        ISerializable hostDefault,
        IReadOnlyList<ProjectSettingCompositionEntry> contributions)
    {
        if (hostDefault is not TSetting target)
        {
            throw new ArgumentException(
                $"Composer '{GetType().FullName}' requires setting type '{typeof(TSetting).FullName}'.",
                nameof(hostDefault));
        }
        var decoded = new List<ProjectSettingContribution<TContribution>>(contributions.Count);
        foreach (ProjectSettingCompositionEntry contribution in contributions)
        {
            TContribution value = serialization.Deserialize<TContribution>(contribution.data);
            if (IsEmpty(value))
            {
                throw new InvalidOperationException(
                    $"Composer '{GetType().FullName}' received an empty persisted contribution from " +
                    $"'{contribution.context.contributorId}'.");
            }
            decoded.Add(new ProjectSettingContribution<TContribution>(
                contribution.context,
                value));
        }
        Compose(target, decoded);
        return target;
    }
}

internal sealed record ProjectSettingCompositionEntry(
    ProjectSettingContributionContext context,
    byte[] data);
