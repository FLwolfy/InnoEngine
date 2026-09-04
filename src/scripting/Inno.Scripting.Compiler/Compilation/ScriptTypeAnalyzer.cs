using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Scripting.Compiler;

internal static class ScriptTypeAnalyzer
{
    private const string C_STABLE_TYPE_ID_ATTRIBUTE =
        "Inno.Extensibility.Types.StableTypeIdAttribute";

    internal static ScriptTypeAnalysisResult Analyze(
        CSharpCompilation compilation,
        IReadOnlyList<ScriptSourceInput> sources,
        IReadOnlyDictionary<string, string> attachableTypes,
        CancellationToken cancellationToken)
    {
        var sourcesByPath = sources.ToDictionary(
            static source => source.sourcePath,
            StringComparer.OrdinalIgnoreCase);
        var discovered = new Dictionary<INamedTypeSymbol, MutableScriptType>(
            SymbolEqualityComparer.Default);
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (!sourcesByPath.TryGetValue(tree.FilePath, out ScriptSourceInput? source))
                continue;
            SemanticModel semanticModel = compilation.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax declaration in tree.GetRoot(cancellationToken)
                         .DescendantNodes()
                         .OfType<TypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol ||
                    symbol.TypeKind != TypeKind.Class ||
                    symbol.IsAbstract ||
                    symbol.IsGenericType ||
                    !TryGetAttachableKind(symbol, attachableTypes, out string kind))
                {
                    continue;
                }

                if (!discovered.TryGetValue(symbol, out MutableScriptType? type))
                {
                    type = new MutableScriptType(symbol, kind);
                    discovered.Add(symbol, type);
                }
                type.declarations.Add(new ScriptDeclaration(source, declaration.Identifier.GetLocation()));
            }
        }

        var diagnostics = new List<ScriptDiagnostic>();
        var manifestEntries = new List<ScriptTypeManifestEntry>();
        var mappings = new List<ScriptTypeMapping>();
        var implicitTypesBySource = discovered.Values
            .Where(static type => GetExplicitStableTypeId(type.symbol) is null)
            .SelectMany(static type => type.declarations
                .Select(declaration => (declaration.source.persistentId, type)))
            .GroupBy(static value => value.persistentId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static value => value.type)
                    .Distinct()
                    .Count());

        foreach (MutableScriptType type in discovered.Values
                     .OrderBy(static value => GetMetadataTypeName(value.symbol), StringComparer.Ordinal))
        {
            string typeName = GetMetadataTypeName(type.symbol);
            string? explicitIdValue = GetExplicitStableTypeId(type.symbol);
            if (explicitIdValue is not null)
            {
                ScriptDeclaration declaration = SelectManifestDeclaration(type);
                if (!Guid.TryParse(explicitIdValue, out Guid explicitId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "INNO2000",
                        ScriptDiagnosticSeverity.Error,
                        $"Type '{typeName}' has invalid StableTypeId '{explicitIdValue}'.",
                        declaration));
                    continue;
                }
                manifestEntries.Add(CreateManifestEntry(
                    type,
                    declaration,
                    explicitId,
                    explicitIdentity: true,
                    canonicalSource: true));
                continue;
            }

            ScriptDeclaration[] matchingDeclarations = type.declarations
                .Where(declaration => string.Equals(
                    GetCanonicalFileName(declaration.source.sourcePath),
                    type.symbol.Name,
                    StringComparison.Ordinal))
                .GroupBy(static declaration => declaration.source.persistentId)
                .Select(static group => group.First())
                .ToArray();
            ScriptDeclaration? canonical = null;
            if (matchingDeclarations.Length == 1)
            {
                canonical = matchingDeclarations[0];
            }
            else if (matchingDeclarations.Length > 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    "INNO2003",
                    ScriptDiagnosticSeverity.Error,
                    $"Partial type '{typeName}' has more than one source file matching its type name. " +
                    "Keep one canonical source or add an explicit StableTypeId.",
                    matchingDeclarations[0]));
            }
            else
            {
                ScriptDeclaration[] distinctSources = type.declarations
                    .GroupBy(static declaration => declaration.source.persistentId)
                    .Select(static group => group.First())
                    .ToArray();
                if (distinctSources.Length == 1 &&
                    implicitTypesBySource.GetValueOrDefault(distinctSources[0].source.persistentId) == 1)
                {
                    canonical = distinctSources[0];
                    diagnostics.Add(CreateDiagnostic(
                        "INNO2002",
                        ScriptDiagnosticSeverity.Warning,
                        $"Attachable type '{typeName}' does not match its canonical script file name. " +
                        "Its source identity is preserved, but matching names are recommended.",
                        canonical));
                }
                else
                {
                    ScriptDeclaration declaration = distinctSources[0];
                    diagnostics.Add(CreateDiagnostic(
                        "INNO2001",
                        ScriptDiagnosticSeverity.Error,
                        $"Attachable type '{typeName}' has no unambiguous canonical script source. " +
                        "Move it to a matching file or add an explicit StableTypeId.",
                        declaration));
                }
            }

            if (canonical is null)
                continue;
            Guid stableTypeId = ScriptTypeIdentity.CreateCanonical(canonical.source.persistentId);
            mappings.Add(new ScriptTypeMapping(typeName, stableTypeId));
            manifestEntries.Add(CreateManifestEntry(
                type,
                canonical,
                stableTypeId,
                explicitIdentity: false,
                canonicalSource: true));
        }

        foreach (IGrouping<Guid, ScriptTypeMapping> collision in mappings
                     .GroupBy(static mapping => mapping.stableTypeId)
                     .Where(static group => group.Count() > 1))
        {
            ScriptTypeMapping[] conflicting = collision.ToArray();
            ScriptTypeManifestEntry entry = manifestEntries.First(value =>
                string.Equals(value.typeName, conflicting[0].typeName, StringComparison.Ordinal));
            diagnostics.Add(new ScriptDiagnostic(
                "INNO2004",
                ScriptDiagnosticSeverity.Error,
                $"Script source '{entry.sourcePath}' owns more than one attachable type identity: " +
                string.Join(", ", conflicting.Select(static value => value.typeName)) +
                ". Keep one canonical type or add explicit StableTypeId attributes.",
                entry.sourcePath,
                entry.line,
                entry.column));
        }

        return new ScriptTypeAnalysisResult(
            new ScriptTypeManifest(compilation.AssemblyName ?? string.Empty, manifestEntries),
            mappings,
            diagnostics);
    }

    internal static string CreateMappingSource(IReadOnlyList<ScriptTypeMapping> mappings)
    {
        var source = new StringBuilder("#nullable enable\n");
        foreach (ScriptTypeMapping mapping in mappings.OrderBy(static value => value.typeName, StringComparer.Ordinal))
        {
            string value = $"{mapping.stableTypeId:D}|{mapping.typeName}";
            source.Append("[assembly: global::System.Reflection.AssemblyMetadataAttribute(")
                .Append(SymbolDisplay.FormatLiteral("Inno.StableTypeId", quote: true))
                .Append(", ")
                .Append(SymbolDisplay.FormatLiteral(value, quote: true))
                .AppendLine(")]" );
        }
        return source.ToString();
    }

    private static bool TryGetAttachableKind(
        INamedTypeSymbol symbol,
        IReadOnlyDictionary<string, string> attachableTypes,
        out string kind)
    {
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (attachableTypes.TryGetValue(GetMetadataTypeName(current), out string? value))
            {
                kind = value;
                return true;
            }
        }
        kind = string.Empty;
        return false;
    }

    private static string? GetExplicitStableTypeId(INamedTypeSymbol symbol)
    {
        AttributeData? attribute = symbol.GetAttributes().FirstOrDefault(value => string.Equals(
            value.AttributeClass?.ToDisplayString(),
            C_STABLE_TYPE_ID_ATTRIBUTE,
            StringComparison.Ordinal));
        return attribute?.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;
    }

    private static string GetMetadataTypeName(INamedTypeSymbol symbol)
    {
        var typeNames = new Stack<string>();
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
            typeNames.Push(current.MetadataName);
        string nestedName = string.Join("+", typeNames);
        return symbol.ContainingNamespace.IsGlobalNamespace
            ? nestedName
            : symbol.ContainingNamespace.ToDisplayString() + "." + nestedName;
    }

    private static string GetCanonicalFileName(string sourcePath)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        return name.EndsWith(".editor", StringComparison.OrdinalIgnoreCase)
            ? name[..^".editor".Length]
            : name;
    }

    private static ScriptDeclaration SelectManifestDeclaration(MutableScriptType type)
        => type.declarations
            .OrderByDescending(declaration => string.Equals(
                GetCanonicalFileName(declaration.source.sourcePath),
                type.symbol.Name,
                StringComparison.Ordinal))
            .ThenBy(static declaration => declaration.source.relativePath, StringComparer.Ordinal)
            .First();

    private static ScriptTypeManifestEntry CreateManifestEntry(
        MutableScriptType type,
        ScriptDeclaration declaration,
        Guid stableTypeId,
        bool explicitIdentity,
        bool canonicalSource)
    {
        FileLinePositionSpan span = declaration.location.GetLineSpan();
        return new ScriptTypeManifestEntry(
            GetMetadataTypeName(type.symbol),
            type.kind,
            stableTypeId,
            declaration.source.persistentId,
            declaration.source.relativePath,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            explicitIdentity,
            canonicalSource);
    }

    private static ScriptDiagnostic CreateDiagnostic(
        string id,
        ScriptDiagnosticSeverity severity,
        string message,
        ScriptDeclaration declaration)
    {
        FileLinePositionSpan span = declaration.location.GetLineSpan();
        return new ScriptDiagnostic(
            id,
            severity,
            message,
            declaration.source.sourcePath,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
    }

    private sealed class MutableScriptType(INamedTypeSymbol symbol, string kind)
    {
        internal INamedTypeSymbol symbol { get; } = symbol;
        internal string kind { get; } = kind;
        internal List<ScriptDeclaration> declarations { get; } = [];
    }

    private sealed record ScriptDeclaration(ScriptSourceInput source, Location location);
}
