using System;

using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Provides generation-bound structured serialization without exposing host registries to importer extensions.
/// </summary>
public sealed class AssetSerializationServices
{
    private readonly Action<AssetDependency>? m_dependencySink;
    private readonly IAssetReferenceResolver? m_references;
    private readonly SerializationRegistry m_serialization;
    private readonly TypeCatalog m_types;

    internal IAssetReferenceResolver references
        => m_references
            ?? throw new InvalidOperationException(
                "Asset reference resolution is unavailable outside an import transaction.");

    internal SerializationRegistry serialization => m_serialization;

    internal TypeCatalog types => m_types;

    internal AssetSerializationServices(
        TypeCatalog types,
        SerializationRegistry serialization,
        IAssetReferenceResolver? references,
        Action<AssetDependency>? dependencySink)
    {
        m_types = types ?? throw new ArgumentNullException(nameof(types));
        m_serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        m_references = references;
        m_dependencySink = dependencySink;
    }

    /// <summary>
    /// Resolves the stable persistent type identity of a registered serializable type.
    /// </summary>
    /// <typeparam name="TValue">
    /// The registered type whose stable identity is required.
    /// </typeparam>
    /// <returns>
    /// The stable type identifier owned by the active extension generation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TValue"/> has no stable type registration.
    /// </exception>
    public Guid GetStableTypeId<TValue>()
        => m_types.GetTypeRef(typeof(TValue)).stableId;

    /// <summary>
    /// Serializes one structured value and declares every encountered asset reference as a runtime dependency.
    /// </summary>
    /// <typeparam name="TValue">
    /// The serializable value contract.
    /// </typeparam>
    /// <param name="value">
    /// The value to serialize through the active converter generation.
    /// </param>
    /// <returns>
    /// Deterministic structured bytes owned by the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public byte[] Serialize<TValue>(TValue value)
        where TValue : class, ISerializable
    {
        ArgumentNullException.ThrowIfNull(value);
        var dependencies = new AssetDependencyCollection();
        SerializationContext context = SerializationContext.empty.With(dependencies);
        byte[] result = m_serialization.Serialize(value, context);
        if (m_dependencySink is not null)
        {
            foreach (AssetDependency dependency in dependencies.dependencies)
                m_dependencySink(dependency);
        }
        return result;
    }

    /// <summary>
    /// Deserializes one structured value against the active importer candidate generation.
    /// </summary>
    /// <typeparam name="TValue">
    /// The serializable value contract.
    /// </typeparam>
    /// <param name="bytes">
    /// Complete structured bytes produced by the common serialization system.
    /// </param>
    /// <returns>
    /// A restored value owned by the caller.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the value contains an asset reference but no candidate resolver is available.
    /// </exception>
    public TValue Deserialize<TValue>(ReadOnlySpan<byte> bytes)
        where TValue : class, ISerializable
    {
        SerializationContext context = m_references is null
            ? SerializationContext.empty
            : SerializationContext.empty.With(m_references);
        return m_serialization.Deserialize<TValue>(bytes, context);
    }
}
