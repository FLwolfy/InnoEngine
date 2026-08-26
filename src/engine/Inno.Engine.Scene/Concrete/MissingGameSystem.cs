using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets.Core;
using Inno.Core.Reflection;

namespace Inno.Engine.Scene;

/// <summary>
/// Preserves the identity, order, and serialized state of a scene system whose managed type is unavailable.
/// </summary>
/// <remarks>
/// The scene serializer and script reload pipeline create this placeholder automatically. It remains disabled
/// and does not retain the unavailable managed <see cref="Type"/> or its collectible assembly load context.
/// </remarks>
[StableTypeId("99342f4e-fb2e-49f9-a6a4-3f4e59d23457")]
[AllowMultipleSystem]
public sealed class MissingGameSystem : GameSystem
{
    private readonly byte[] m_serializedState;
    private readonly AssetDependency[] m_dependencies;
    private IReadOnlyDictionary<Guid, Guid> m_referenceAliases =
        new Dictionary<Guid, Guid>();

    internal MissingGameSystem(
        TypeRef missingType,
        string missingTypeName,
        ReadOnlySpan<byte> serializedState,
        IReadOnlyList<AssetDependency>? dependencies = null)
    {
        if (missingType.stableId == Guid.Empty)
            throw new ArgumentException("The missing system type identity cannot be empty.", nameof(missingType));
        this.missingType = missingType;
        this.missingTypeName = string.IsNullOrWhiteSpace(missingTypeName)
            ? missingType.stableId.ToString("D")
            : missingTypeName;
        m_serializedState = serializedState.ToArray();
        m_dependencies = dependencies?.ToArray() ?? [];
        enabled = false;
    }

    /// <summary>
    /// Gets the logical identity of the unavailable system type.
    /// </summary>
    public TypeRef missingType { get; }

    /// <summary>
    /// Gets the last known managed type name for diagnostics and editor presentation.
    /// </summary>
    public string missingTypeName { get; }

    internal byte[] CaptureSerializedState() => (byte[])m_serializedState.Clone();

    internal IReadOnlyDictionary<Guid, Guid> referenceAliases => m_referenceAliases;

    internal IReadOnlyList<AssetDependency> dependencies => m_dependencies;

    internal void SetReferenceAliases(IReadOnlyDictionary<Guid, Guid> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        m_referenceAliases = aliases;
    }
}
