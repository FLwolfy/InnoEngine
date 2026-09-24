using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Pipeline;
using Inno.Core.Serialization;

namespace Inno.UI.Assets;

/// <summary>
/// Imports implementation-neutral UTF-8 UI source using explicit sidecar settings.
/// </summary>
[AssetImporter("inno.ui.document")]
public sealed class UiDocumentImporter : AssetImporter<UiDocumentAsset>
{
    /// <inheritdoc />
    public override IReadOnlyList<string> supportedExtensions { get; } = [".iuidocument"];

    /// <inheritdoc />
    public override ISerializable CreateImportSettings() => new UiDocumentImportSettings();

    /// <inheritdoc />
    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<UiDocumentAsset> output,
        CancellationToken cancellationToken)
    {
        var settings = context.importSettings as UiDocumentImportSettings
            ?? throw new InvalidOperationException("UI source requires its standard import settings.");
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.languageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.implementationId);
        await UiDocumentImportPipeline.ImportAsync(
            context,
            output,
            new UiDocumentLanguageId(settings.languageId),
            settings.implementationId,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Runs the shared neutral import transaction for concrete extension importers.</summary>
public static class UiDocumentImportPipeline
{
    /// <summary>Validates and emits a deterministic runtime payload.</summary>
    /// <param name="context">Source import context.</param>
    /// <param name="output">Candidate writer.</param>
    /// <param name="language">Explicit source language.</param>
    /// <param name="implementationId">Explicit runtime implementation.</param>
    /// <param name="cancellationToken">Cancellation observed by artifact writes.</param>
    /// <returns>An operation completing after the payload is staged.</returns>
    public static async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<UiDocumentAsset> output,
        UiDocumentLanguageId language,
        string implementationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
        string text = context.ReadUtf8Text();
        using var frontends = new UiDocumentFrontendRegistry(context.types);
        UiDocumentAnalysis analysis = frontends.Analyze(language, new(context.assetPath.ToString(), text));
        if (!analysis.succeeded)
            throw new InvalidDataException(string.Join("\n", analysis.diagnostics.Select(static diagnostic =>
                $"{diagnostic.line}:{diagnostic.column}: {diagnostic.code}: {diagnostic.message}")));
        var source = new UiDocumentSource(language, analysis.text!, context.assetPath.ToString());
        output.SetAsset(new UiDocumentAsset());
        await output.WriteArtifactAsync(
            "runtime",
            UiDocumentAsset.CreateRuntimePayload(implementationId, source),
            cancellationToken).ConfigureAwait(false);
    }
}
