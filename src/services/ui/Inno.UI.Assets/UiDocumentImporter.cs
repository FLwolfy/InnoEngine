using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Pipeline;
using Inno.Assets;
using Inno.Core.Serialization;
using Inno.Text;

namespace Inno.UI.Assets;

/// <summary>
/// Imports implementation-neutral UTF-8 UI source using explicit sidecar settings.
/// </summary>
[AssetImporter("inno.ui.document")]
public sealed class UiDocumentImporter : AssetImporter<UiDocumentAsset>
{
    /// <summary>
    /// Gets the normalized source extensions accepted by this importer.
    /// </summary>
    public override IReadOnlyList<string> supportedExtensions { get; } = [".iuidocument"];

    /// <summary>
    /// Creates an import settings using this implementation's validated inputs.
    /// </summary>
    /// <returns>
    /// The validated iserializable that represents the completed operation.
    /// </returns>
    public override ISerializable CreateImportSettings() => new UiDocumentImportSettings();

    /// <summary>
    /// Imports source content into a validated runtime asset and artifact set.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <param name="output">
    /// The import output writer that receives runtime data and dependency declarations.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that cancels the operation before it commits.
    /// </param>
    /// <returns>
    /// An asynchronous operation that completes after all requested work has finished.
    /// </returns>
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

/// <summary>
/// Coordinates source analysis, font dependencies, and runtime UI asset emission.
/// </summary>
public static class UiDocumentImportPipeline
{
    /// <summary>
    /// Validates and emits a deterministic runtime payload.
    /// </summary>
    /// <param name="context">
    /// Source import context.
    /// </param>
    /// <param name="output">
    /// Candidate writer.
    /// </param>
    /// <param name="language">
    /// Explicit source language.
    /// </param>
    /// <param name="implementationId">
    /// Explicit runtime implementation.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation observed by artifact writes.
    /// </param>
    /// <returns>
    /// An operation completing after the payload is staged.
    /// </returns>
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
        UiDocumentAnalysis analysis = frontends.Analyze(language,
            new(context.assetPath.ToString(), text, context.ReadSourceUtf8Text));
        if (!analysis.succeeded)
            throw new InvalidDataException(string.Join("\n", analysis.diagnostics.Select(static diagnostic =>
                $"{diagnostic.line}:{diagnostic.column}: {diagnostic.code}: {diagnostic.message}")));
        var fonts = new List<UiDocumentFontFace>(analysis.fonts.Count);
        foreach (UiDocumentFontDeclaration declaration in analysis.fonts)
        {
            FontAsset font = context.ResolveDependency<FontAsset>(AssetPath.Parse(declaration.assetPath));
            if (font.isMissing || font.metadata is not { faceCount: > 0 })
                throw new InvalidDataException($"UI font asset '{declaration.assetPath}' has no usable face.");
            context.DependsOnArtifact(font.identity.persistentId);
            fonts.Add(new UiDocumentFontFace(font.identity.persistentId,
                declaration.family, declaration.style, declaration.weight));
        }
        string sourceUri = $"innoasset://{context.assetPath.source.value}/{context.assetPath.localPath}";
        var source = new UiDocumentSource(language, analysis.text!, sourceUri);
        output.SetAsset(new UiDocumentAsset());
        await output.WriteArtifactAsync(
            "runtime",
            UiDocumentAsset.CreateRuntimePayload(implementationId, source, fonts),
            cancellationToken).ConfigureAwait(false);
    }
}
