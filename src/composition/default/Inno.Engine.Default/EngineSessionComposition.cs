using System;
using Inno.Adapter;
using Inno.Adapter.Input;
using Inno.Assets;
using Inno.Audio;
using Inno.Runtime;
using Inno.Runtime.Contracts;

namespace Inno.Engine.Default;

/// <summary>
/// Supplies strongly typed product-owned inputs to the default engine composition without exposing concrete backends.
/// </summary>
public sealed class EngineSessionComposition
{
    /// <summary>
    /// Captures the inputs borrowed during synchronous default subsystem construction.
    /// </summary>
    /// <param name="session">
    /// The isolated owner receiving these subsystems.
    /// </param>
    /// <param name="adapters">
    /// The host's neutral adapter catalog.
    /// </param>
    /// <param name="selection">
    /// The user's backend selections interpreted only by adapter factories.
    /// </param>
    /// <param name="inputSource">
    /// The host input source used to create isolated session readers.
    /// </param>
    /// <param name="artifacts">
    /// The selected authoring or deployed artifact lookup.
    /// </param>
    /// <param name="audioSettings">
    /// The portable audio defaults, captured during factory activation.
    /// </param>
    /// <param name="audioOverride">
    /// An optional product-specific audio owner, such as Editor preview/Play isolation.
    /// </param>
    public EngineSessionComposition(RuntimeSession session, IAdapterCatalog adapters, AdapterSelection selection,
        IInputEventSource inputSource, IAssetArtifactLookup artifacts, Func<AudioProjectSettings> audioSettings,
        IRuntimeSubsystemFactory? audioOverride = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        this.selection = selection;
        this.inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
        this.artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        this.audioSettings = audioSettings ?? throw new ArgumentNullException(nameof(audioSettings));
        this.audioOverride = audioOverride;
    }

    /// <summary>
    /// Gets the isolated session selected by the product.
    /// </summary>
    public RuntimeSession session { get; }
    /// <summary>
    /// Gets the backend-neutral adapter factory catalog.
    /// </summary>
    public IAdapterCatalog adapters { get; }
    /// <summary>
    /// Gets portable backend selection values.
    /// </summary>
    public AdapterSelection selection { get; }
    /// <summary>
    /// Gets the host input source shared by isolated session readers.
    /// </summary>
    public IInputEventSource inputSource { get; }
    /// <summary>
    /// Gets immutable encoded content artifacts from the selected host environment.
    /// </summary>
    public IAssetArtifactLookup artifacts { get; }
    /// <summary>
    /// Gets the control-thread source of portable audio defaults.
    /// </summary>
    public Func<AudioProjectSettings> audioSettings { get; }
    /// <summary>
    /// Gets an optional product-specific audio owner factory.
    /// </summary>
    public IRuntimeSubsystemFactory? audioOverride { get; }
}
