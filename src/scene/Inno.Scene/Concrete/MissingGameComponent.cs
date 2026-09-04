using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Assets;
using Inno.Extensibility.Types;

namespace Inno.Scene;

/// <summary>
/// Preserves the identity, order, and serialized state of a component whose managed type is unavailable.
/// </summary>
/// <remarks>
/// The scene serializer and script reload pipeline create this placeholder automatically. It does not retain
/// the unavailable managed <see cref="Type"/> or any object from its collectible assembly load context.
/// </remarks>
[StableTypeId("5f53a311-d54f-4ad1-9f2d-a9bd0da90844")]
[AllowMultipleComponent]
public sealed class MissingGameComponent : GameComponent
{
    private readonly byte[] m_serializedState;
    private readonly AssetDependency[] m_dependencies;
    private IReadOnlyDictionary<Guid, Guid> m_referenceAliases =
        new Dictionary<Guid, Guid>();

    internal MissingGameComponent(
        TypeRef missingType,
        string missingTypeName,
        ReadOnlySpan<byte> serializedState,
        IReadOnlyList<AssetDependency>? dependencies = null)
    {
        if (missingType.stableId == Guid.Empty)
            throw new ArgumentException("The missing component type identity cannot be empty.", nameof(missingType));
        this.missingType = missingType;
        this.missingTypeName = string.IsNullOrWhiteSpace(missingTypeName)
            ? missingType.stableId.ToString("D")
            : missingTypeName;
        m_serializedState = serializedState.ToArray();
        m_dependencies = dependencies?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets the logical identity of the unavailable component type.
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
