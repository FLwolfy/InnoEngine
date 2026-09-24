using System;
using System.Collections.Generic;
using System.IO;

using Inno.Assets;
using Inno.Input;
using Inno.Runtime;
using Inno.Runtime.Contracts;
using Inno.Text;

namespace Inno.UI.Runtime;

/// <summary>
/// Owns retained UI contexts, imported font leases, and current-frame input for one runtime session.
/// </summary>
public sealed class UiRuntime : RuntimeSubsystem, IUiService
{
    private readonly IUiBackend m_backend;
    private readonly IAssetArtifactLookup m_artifacts;
    private readonly Dictionary<FontRegistration, ArtifactLease> m_fonts = [];
    private InputSnapshot m_input = InputSnapshot.empty;
    private bool m_disposed;

    /// <summary>
    /// Creates a UI runtime and assumes ownership of its backend.
    /// </summary>
    /// <param name="backend">The retained-mode UI backend.</param>
    /// <param name="artifacts">The immutable asset artifact lookup.</param>
    public UiRuntime(IUiBackend backend, IAssetArtifactLookup artifacts)
    {
        m_backend = backend ?? throw new ArgumentNullException(nameof(backend));
        m_artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    /// <summary>
    /// Captures the input service snapshot and binds script-facing UI for the complete runtime frame.
    /// </summary>
    /// <param name="frame">The current runtime frame.</param>
    protected override void OnBeginFrame(RuntimeFrame frame)
    {
        m_input = InputExecutionContext.current.snapshot;
        OwnFrameScope(EnterExecutionScope());
    }

    /// <summary>
    /// Binds this runtime to the current asynchronous execution context.
    /// </summary>
    /// <returns>The caller-owned binding scope.</returns>
    public IDisposable EnterExecutionScope()
    {
        EnsureActive();
        return UiExecutionContext.EnterScope(this);
    }

    /// <inheritdoc />
    public UiContextHandle CreateContext(UiContextOptions options)
    {
        EnsureActive();
        return m_backend.CreateContext(options);
    }

    /// <inheritdoc />
    public void DestroyContext(UiContextHandle context)
    {
        EnsureActive();
        m_backend.DestroyContext(context);
    }

    /// <inheritdoc />
    public void SetViewport(UiContextHandle context, int width, int height, float density = 1f)
    {
        EnsureActive();
        m_backend.SetViewport(context, width, height, density);
    }

    /// <inheritdoc />
    public UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureActive();
        EnsureLanguageSupported(source.language);
        return m_backend.LoadDocument(context, source);
    }

    /// <inheritdoc />
    public UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentAsset document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureActive();
        if (document.isMissing)
            throw new InvalidOperationException("The UI document asset is missing.");
        UiDocumentSource source = document.source
            ?? throw new InvalidOperationException("The UI document has no imported runtime payload.");
        if (!string.Equals(document.implementationId, m_backend.implementationId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"UI document implementation '{document.implementationId}' cannot be loaded by selected backend '{m_backend.implementationId}'.");
        EnsureLanguageSupported(source.language);
        return m_backend.LoadDocument(context, source);
    }

    /// <inheritdoc />
    public void ShowDocument(UiContextHandle context, UiDocumentHandle document)
    {
        EnsureActive();
        m_backend.ShowDocument(context, document);
    }

    /// <inheritdoc />
    public void HideDocument(UiContextHandle context, UiDocumentHandle document)
    {
        EnsureActive();
        m_backend.HideDocument(context, document);
    }

    /// <inheritdoc />
    public void CloseDocument(UiContextHandle context, UiDocumentHandle document)
    {
        EnsureActive();
        m_backend.CloseDocument(context, document);
    }

    /// <inheritdoc />
    public bool SetText(UiContextHandle context, UiDocumentHandle document, string elementId, string text)
    {
        EnsureActive();
        return m_backend.SetText(context, document, elementId, text);
    }

    /// <inheritdoc />
    public bool SetContent(
        UiContextHandle context,
        UiDocumentHandle document,
        string elementId,
        UiDocumentFragment content)
    {
        EnsureActive();
        EnsureLanguageSupported(content.language);
        return m_backend.SetContent(context, document, elementId, content);
    }

    /// <inheritdoc />
    public bool SetAttribute(
        UiContextHandle context,
        UiDocumentHandle document,
        string elementId,
        string name,
        string value)
    {
        EnsureActive();
        return m_backend.SetAttribute(context, document, elementId, name, value);
    }

    /// <inheritdoc />
    public bool SetClass(
        UiContextHandle context,
        UiDocumentHandle document,
        string elementId,
        string className,
        bool active)
    {
        EnsureActive();
        return m_backend.SetClass(context, document, elementId, className, active);
    }

    /// <inheritdoc />
    public void RegisterFont(
        FontAsset font,
        string family,
        int faceIndex = 0,
        TextFontStyle style = TextFontStyle.Normal,
        int weight = 400,
        bool fallback = false)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        if (weight is < 100 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(weight));
        EnsureActive();
        if (font.isMissing)
            throw new InvalidOperationException("The font asset is missing.");
        FontMetadata metadata = font.metadata
            ?? throw new InvalidOperationException("The font has no imported runtime metadata.");
        if (metadata.faceCount < 1)
            throw new InvalidOperationException("The font contains no usable faces.");
        if (faceIndex >= metadata.faceCount)
            throw new ArgumentOutOfRangeException(nameof(faceIndex), faceIndex, "The requested font face is outside the imported collection.");
        if (faceIndex != 0 && !m_backend.capabilities.supportsFontCollectionFaces)
            throw new UiCapabilityUnavailableException(UiBackendCapability.FontCollectionFaceSelection);

        var registration = new FontRegistration(
            font.identity.persistentId,
            font.contentVersion,
            faceIndex,
            family.Trim(),
            style,
            weight,
            fallback);
        if (m_fonts.ContainsKey(registration))
            return;

        ArtifactLease artifact = m_artifacts.AcquireArtifact(font.identity.persistentId, "font-data");
        try
        {
            byte[] bytes = File.ReadAllBytes(artifact.info.absolutePath);
            m_backend.RegisterFont(new UiFontRegistration(
                bytes,
                faceIndex,
                registration.family,
                style,
                weight,
                fallback));
            m_fonts.Add(registration, artifact);
        }
        catch
        {
            artifact.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void RegisterTexture(UiContextHandle context, string source, UiTextureData texture)
    {
        EnsureActive();
        m_backend.RegisterTexture(context, source, texture);
    }

    /// <inheritdoc />
    public void Update(UiContextHandle context)
    {
        EnsureActive();
        m_backend.Update(context, new UiInputSnapshot(
            m_input.mousePosition,
            m_input.scrollDelta,
            m_input.modifiers,
            m_input.keysPressed,
            m_input.keysReleased,
            m_input.mouseButtonsPressed,
            m_input.mouseButtonsReleased,
            m_input.textInput));
    }

    /// <inheritdoc />
    public UiRenderFrame Render(UiContextHandle context)
    {
        EnsureActive();
        return m_backend.Render(context);
    }

    /// <inheritdoc />
    public IReadOnlyList<UiEvent> DrainEvents(UiContextHandle context)
    {
        EnsureActive();
        return m_backend.DrainEvents(context);
    }

    /// <summary>
    /// Releases native UI state before releasing retained font artifacts.
    /// </summary>
    protected override void OnStop()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        List<Exception> failures = [];
        try { m_backend.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        foreach (ArtifactLease artifact in m_fonts.Values)
        {
            try { artifact.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        m_fonts.Clear();
        if (failures.Count > 0)
            throw new AggregateException("UI runtime retirement failed.", failures);
    }

    private void EnsureActive() => ObjectDisposedException.ThrowIf(m_disposed, this);

    private void EnsureLanguageSupported(UiDocumentLanguageId language)
    {
        if (!m_backend.capabilities.Supports(language))
            throw new NotSupportedException(
                $"UI document language '{language}' is not supported by selected backend '{m_backend.implementationId}'.");
    }

    private readonly record struct FontRegistration(
        Guid persistentId,
        long version,
        int faceIndex,
        string family,
        TextFontStyle style,
        int weight,
        bool fallback);
}
