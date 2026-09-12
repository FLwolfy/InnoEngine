using System;

using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Scripting.Api;

namespace Inno.Assets.Pipeline;

/// <summary>
/// Provides generation-bound services for one editable asset source export.
/// </summary>
public sealed class AssetExportContext
{
    internal AssetExportContext(
        TypeCatalog types,
        SerializationRegistry serialization,
        Inno.Assets.IAssetArtifactLookup artifacts)
    {
        this.types = types ?? throw new ArgumentNullException(nameof(types));
        this.serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        this.artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        services = new AssetSerializationServices(types, serialization, references: null, dependencySink: null);
    }

    /// <summary>
    /// Gets the type catalog bound to the active export generation.
    /// </summary>
    [ScriptingApiIgnore]
    public TypeCatalog types { get; }

    /// <summary>
    /// Gets the serialization registry bound to the active export generation.
    /// </summary>
    [ScriptingApiIgnore]
    public SerializationRegistry serialization { get; }

    /// <summary>
    /// Gets the narrow structured serialization API bound to this export generation.
    /// </summary>
    public AssetSerializationServices services { get; }

    /// <summary>Gets the owner-bound immutable outputs used to reconstruct editable sources.</summary>
    [ScriptingApiIgnore]
    public Inno.Assets.IAssetArtifactLookup artifacts { get; }
}
