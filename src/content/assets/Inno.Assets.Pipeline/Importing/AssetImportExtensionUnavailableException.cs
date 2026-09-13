using System;

namespace Inno.Assets.Pipeline;

/// <summary>Reports a required authoring extension absent from the current generation.</summary>
/// <remarks>The host may defer this condition during discovery; it remains an import failure once discovery is complete.</remarks>
public sealed class AssetImportExtensionUnavailableException : InvalidOperationException
{
    /// <summary>Describes a missing extension using generation-neutral identities.</summary>
    /// <param name="extensionKind">The stable extension protocol identity.</param>
    /// <param name="extensionId">The required implementation's stable identity.</param>
    public AssetImportExtensionUnavailableException(string extensionKind, string extensionId)
        : base($"Required authoring extension '{extensionKind}/{extensionId}' is unavailable. Source and last-good artifacts are retained.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        this.extensionKind = extensionKind;
        this.extensionId = extensionId;
    }

    /// <summary>Gets the stable extension protocol identity.</summary>
    public string extensionKind { get; }

    /// <summary>Gets the required implementation's stable identity.</summary>
    public string extensionId { get; }
}
