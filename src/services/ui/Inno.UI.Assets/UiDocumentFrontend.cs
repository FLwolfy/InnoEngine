using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Inno.Extensibility.Types;

namespace Inno.UI.Assets;

/// <summary>Contains immutable source text supplied to a UI language frontend.</summary>
/// <param name="assetPath">Original asset mount path.</param>
/// <param name="text">Strictly decoded UTF-8 text.</param>
public sealed record UiDocumentSourceFile(string assetPath, string text);

/// <summary>Reports a language-neutral source problem.</summary>
/// <param name="code">Stable diagnostic code.</param>
/// <param name="message">Actionable description.</param>
/// <param name="line">One-based source line.</param>
/// <param name="column">One-based source column.</param>
public sealed record UiDocumentDiagnostic(string code, string message, int line, int column);

/// <summary>Returns validated text without retaining parser or implementation objects.</summary>
public sealed class UiDocumentAnalysis
{
    /// <summary>Creates a frozen analysis result.</summary>
    /// <param name="text">Validated canonical text, or null on failure.</param>
    /// <param name="diagnostics">Complete frontend diagnostics.</param>
    public UiDocumentAnalysis(string? text, IEnumerable<UiDocumentDiagnostic> diagnostics)
    {
        this.text = text;
        this.diagnostics = new ReadOnlyCollection<UiDocumentDiagnostic>(diagnostics?.ToArray()
            ?? throw new ArgumentNullException(nameof(diagnostics)));
    }
    /// <summary>Gets canonical source text, or null after failure.</summary>
    public string? text { get; }
    /// <summary>Gets frozen diagnostics.</summary>
    public IReadOnlyList<UiDocumentDiagnostic> diagnostics { get; }
    /// <summary>Gets whether the frontend accepted the source.</summary>
    public bool succeeded => text is not null && diagnostics.Count == 0;
}

/// <summary>Validates one UI document language without creating runtime UI objects.</summary>
public interface IUiDocumentFrontend
{
    /// <summary>Gets the open source-language identity.</summary>
    UiDocumentLanguageId languageId { get; }
    /// <summary>Analyzes one immutable source snapshot.</summary>
    /// <param name="source">Original source snapshot.</param>
    /// <returns>Canonical text or neutral diagnostics.</returns>
    UiDocumentAnalysis Analyze(UiDocumentSourceFile source);
}

/// <summary>Owns one immutable UI language registration snapshot.</summary>
public sealed class UiDocumentFrontendCatalog
{
    private readonly Dictionary<UiDocumentLanguageId, IUiDocumentFrontend> m_frontends = [];
    /// <summary>Captures a validated provider set.</summary>
    /// <param name="frontends">Providers owned by the authoring generation.</param>
    public UiDocumentFrontendCatalog(IEnumerable<IUiDocumentFrontend> frontends)
    {
        ArgumentNullException.ThrowIfNull(frontends);
        foreach (IUiDocumentFrontend frontend in frontends)
            if (frontend is null || !frontend.languageId.isValid || !m_frontends.TryAdd(frontend.languageId, frontend))
                throw new ArgumentException("UI document frontends require unique, assigned language IDs.", nameof(frontends));
        languageIds = new ReadOnlyCollection<UiDocumentLanguageId>([.. m_frontends.Keys]);
    }
    /// <summary>Gets registered language identities.</summary>
    public IReadOnlyList<UiDocumentLanguageId> languageIds { get; }
    internal IEnumerable<IUiDocumentFrontend> providers => m_frontends.Values;
    /// <summary>Analyzes source using an explicit language.</summary>
    /// <param name="language">Registered language identity.</param>
    /// <param name="source">Immutable source snapshot.</param>
    /// <returns>Neutral analysis result.</returns>
    public UiDocumentAnalysis Analyze(UiDocumentLanguageId language, UiDocumentSourceFile source)
        => m_frontends.TryGetValue(language, out IUiDocumentFrontend? frontend)
            ? frontend.Analyze(source) ?? throw new InvalidOperationException($"UI language '{language}' returned no analysis.")
            : throw new NotSupportedException($"UI document language '{language}' is unavailable.");
}

/// <summary>Discovers UI language frontends through the shared type-generation transaction.</summary>
public sealed class UiDocumentFrontendRegistry : TypeRegistry<UiDocumentFrontendCatalog>
{
    private readonly TypeCatalog m_types;
    /// <summary>Registers with the owner type catalog.</summary>
    /// <param name="types">Catalog that outlives this registry.</param>
    public UiDocumentFrontendRegistry(TypeCatalog types) : base(types) => m_types = types;
    /// <summary>Analyzes source while preventing provider retirement.</summary>
    /// <param name="language">Exact language identity.</param>
    /// <param name="source">Immutable source snapshot.</param>
    /// <returns>Detached neutral analysis.</returns>
    public UiDocumentAnalysis Analyze(UiDocumentLanguageId language, UiDocumentSourceFile source)
    {
        using IDisposable operation = m_types.AcquireOperation("Analyze UI document source");
        return current.Analyze(language, source);
    }
    /// <inheritdoc />
    protected override UiDocumentFrontendCatalog Build(TypeCacheSnapshot types)
    {
        var providers = new List<IUiDocumentFrontend>();
        foreach (Type type in types.GetTypesImplementing<IUiDocumentFrontend>()
                     .Select(reference => reference.Resolve(types))
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal))
            providers.Add(CreateExtension<IUiDocumentFrontend>(type));
        return new UiDocumentFrontendCatalog(providers);
    }
    /// <inheritdoc />
    protected override void DisposeSnapshot(UiDocumentFrontendCatalog snapshot) => DisposeExtensions(snapshot.providers);
}
