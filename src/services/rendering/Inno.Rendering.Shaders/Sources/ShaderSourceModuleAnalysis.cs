using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inno.Core.Diagnostics;

namespace Inno.Rendering.Shaders;

/// <summary>Records an include resolution edge so compilation never reinterprets authoring paths against live files.</summary>
/// <param name="includingFile">Source that requested the include.</param>
/// <param name="include">Original include spelling passed to the source resolver.</param>
/// <param name="resolvedPath">Exact source identity within the frozen file set.</param>
public sealed record ShaderSourceInclude(string includingFile, string include, string resolvedPath);

/// <summary>Identifies one explicitly selected implementation and preprocessing configuration of a source module.</summary>
public sealed class ShaderSourceImplementationRequest
{
    /// <summary>Creates a candidate without inferring a language or adapter from its filename.</summary>
    /// <param name="implementationId">Unique owner-defined key, including the target or variant when applicable.</param>
    /// <param name="languageId">Registered source language identity.</param>
    /// <param name="source">Source function and candidate-scoped dependency resolver.</param>
    public ShaderSourceImplementationRequest(string implementationId, string languageId, ShaderSourceRequest source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        this.implementationId = implementationId;
        this.languageId = languageId;
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Gets the stable owner-defined implementation/configuration key.</summary>
    public string implementationId { get; }
    /// <summary>Gets the explicitly selected language identity.</summary>
    public string languageId { get; }
    /// <summary>Gets the transient candidate request, which must not be persisted or retained after analysis.</summary>
    public ShaderSourceRequest source { get; }
}

/// <summary>Freezes a parsed implementation and its complete source inputs without retaining a frontend or resolver.</summary>
public sealed class ShaderSourceImplementationAnalysis
{
    internal ShaderSourceImplementationAnalysis(ShaderSourceImplementationRequest request, ShaderSourceAnalysis analysis,
        IEnumerable<ShaderSourceFile> files, IEnumerable<ShaderSourceInclude> includes)
    {
        implementationId = request.implementationId;
        languageId = request.languageId;
        sourcePath = request.source.source.assetPath;
        entryPoint = request.source.entryPoint;
        defines = request.source.defines;
        this.analysis = analysis;
        sources = Array.AsReadOnly(files.OrderBy(static file => file.assetPath, StringComparer.Ordinal).ToArray());
        this.includes = Array.AsReadOnly(includes.OrderBy(static edge => edge.includingFile, StringComparer.Ordinal)
            .ThenBy(static edge => edge.include, StringComparer.Ordinal).ToArray());
        contentHash = ComputeContentHash();
    }

    /// <summary>Gets the stable implementation/configuration key.</summary>
    public string implementationId { get; }
    /// <summary>Gets the selected source language.</summary>
    public string languageId { get; }
    /// <summary>Gets the root source path in the frozen source set.</summary>
    public string sourcePath { get; }
    /// <summary>Gets the implementation's selected callable function name.</summary>
    public string entryPoint { get; }
    /// <summary>Gets the immutable preprocessing inputs used for this implementation.</summary>
    public IReadOnlyDictionary<string, string> defines { get; }
    /// <summary>Gets every successfully read source, including the root, ordered by path.</summary>
    public IReadOnlyList<ShaderSourceFile> sources { get; }
    /// <summary>Gets the original resolver's frozen include edges, including aliases and mounted source paths.</summary>
    public IReadOnlyList<ShaderSourceInclude> includes { get; }
    /// <summary>Gets the parsed interface and implementation-local diagnostics.</summary>
    public ShaderSourceAnalysis analysis { get; }
    /// <summary>
    /// Gets a deterministic source-input hash. A compiler cache must additionally include its toolchain,
    /// target, graph semantics and binding layout; this hash alone is not a compiled artifact key.
    /// </summary>
    public string contentHash { get; }

    /// <summary>Recreates a compiler input backed only by immutable captured sources and resolution edges.</summary>
    /// <returns>A request with no filesystem access or reference to the original frontend, resolver or generation.</returns>
    public ShaderSourceRequest CreateSourceRequest()
        => new(sources.Single(file => file.assetPath == sourcePath), entryPoint, new FrozenResolver(sources, includes), defines);

    private string ComputeContentHash()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(languageId);
        writer.Write(sourcePath);
        writer.Write(entryPoint);
        writer.Write(defines.Count);
        foreach ((string key, string value) in defines.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            writer.Write(key);
            writer.Write(value);
        }
        writer.Write(sources.Count);
        foreach (ShaderSourceFile file in sources)
        {
            writer.Write(file.assetPath);
            writer.Write(file.text);
        }
        writer.Write(includes.Count);
        foreach (ShaderSourceInclude edge in includes)
        {
            writer.Write(edge.includingFile);
            writer.Write(edge.include);
            writer.Write(edge.resolvedPath);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private sealed class FrozenResolver(IReadOnlyList<ShaderSourceFile> sources, IReadOnlyList<ShaderSourceInclude> includes) : IShaderSourceResolver
    {
        public ShaderSourceFile ReadInclude(string includingFile, string include)
        {
            ShaderSourceInclude edge = includes.FirstOrDefault(value => value.includingFile == includingFile && value.include == include)
                ?? throw new InvalidOperationException($"Include '{include}' from '{includingFile}' was not captured during source analysis.");
            return sources.Single(file => file.assetPath == edge.resolvedPath);
        }
    }
}

/// <summary>Reports whether every supplied implementation and variant has the same callable graph interface.</summary>
public sealed class ShaderSourceModuleAnalysis
{
    internal ShaderSourceModuleAnalysis(IEnumerable<ShaderSourceImplementationAnalysis> implementations,
        IEnumerable<ShaderSourceDiagnostic> diagnostics)
    {
        this.implementations = Array.AsReadOnly(implementations.ToArray());
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        succeeded = this.implementations.Count != 0 && this.implementations.All(static item => item.analysis.succeeded)
            && this.diagnostics.All(static item => item.severity != DiagnosticSeverity.Error);
        function = succeeded ? this.implementations[0].analysis.function : null;
    }

    /// <summary>Gets all implementation snapshots, including failed or unavailable implementations.</summary>
    public IReadOnlyList<ShaderSourceImplementationAnalysis> implementations { get; }
    /// <summary>Gets implementation and cross-implementation diagnostics with original source positions.</summary>
    public IReadOnlyList<ShaderSourceDiagnostic> diagnostics { get; }
    /// <summary>Gets the common interface only when all supplied implementations agree and are valid.</summary>
    public ShaderSourceFunction? function { get; }
    /// <summary>Gets whether interface analysis succeeded; native compilation is a separate gate.</summary>
    public bool succeeded { get; }
}

internal sealed class ShaderSourceSnapshotResolver : IShaderSourceResolver
{
    private readonly IShaderSourceResolver m_resolver;
    private readonly Dictionary<string, ShaderSourceFile> m_files = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), ShaderSourceInclude> m_includes = [];

    internal ShaderSourceSnapshotResolver(ShaderSourceRequest request)
    {
        m_resolver = request.resolver;
        m_files.Add(request.source.assetPath, request.source);
    }

    internal IEnumerable<ShaderSourceFile> files => m_files.Values;
    internal IEnumerable<ShaderSourceInclude> includes => m_includes.Values;

    public ShaderSourceFile ReadInclude(string includingFile, string include)
    {
        if (!m_files.ContainsKey(includingFile))
            throw new InvalidOperationException("An include must originate from an already captured source.");
        ShaderSourceFile file = m_resolver.ReadInclude(includingFile, include)
            ?? throw new InvalidOperationException("The source resolver returned no source snapshot.");
        ArgumentException.ThrowIfNullOrWhiteSpace(file.assetPath);
        ArgumentNullException.ThrowIfNull(file.text);
        if (m_files.TryGetValue(file.assetPath, out ShaderSourceFile? previous) && previous.text != file.text)
            throw new InvalidOperationException($"Source '{file.assetPath}' changed during module analysis.");
        if (m_includes.TryGetValue((includingFile, include), out ShaderSourceInclude? edge) && edge.resolvedPath != file.assetPath)
            throw new InvalidOperationException($"Include '{include}' changed resolution during module analysis.");
        m_includes[(includingFile, include)] = new(includingFile, include, file.assetPath);
        m_files[file.assetPath] = file;
        return file;
    }
}
