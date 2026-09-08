using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Scripting.Compiler;

internal sealed class ScriptApiUsingRewriter : CSharpSyntaxRewriter
{
    private readonly IReadOnlyDictionary<string, string[]> m_mappings;
    private readonly IReadOnlyDictionary<string, ScriptApiTypeMapping[]> m_typeMappings;
    private readonly IReadOnlyDictionary<string, string> m_qualifiedTypeMappings;
    private IReadOnlyDictionary<string, string> m_activeSimpleTypeMappings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal ScriptApiUsingRewriter(
        IReadOnlyList<ScriptApiNamespaceMapping> mappings,
        IReadOnlyList<ScriptApiTypeMapping> typeMappings)
    {
        m_mappings = mappings
            .GroupBy(static mapping => mapping.apiNamespace, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static mapping => mapping.implementationNamespace)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        m_typeMappings = typeMappings
            .Where(static mapping => mapping.arity == 0)
            .GroupBy(static mapping => mapping.apiNamespace, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .GroupBy(
                        static mapping => (
                            mapping.apiName,
                            mapping.implementationNamespace,
                            mapping.implementationName))
                    .Select(static mappings => mappings.First())
                    .OrderBy(static mapping => mapping.apiName, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        m_qualifiedTypeMappings = typeMappings
            .GroupBy(
                static mapping => mapping.apiNamespace + "." + mapping.apiName,
                StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static mapping => mapping.implementationNamespace + "." + mapping.implementationName)
                    .Distinct(StringComparer.Ordinal)
                    .Single(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Rewrites logical API imports and type expressions declared at compilation-unit scope.
    /// </summary>
    /// <param name="node">
    /// The source compilation unit to rewrite.
    /// </param>
    /// <returns>
    /// The rewritten compilation unit, or <see langword="null"/> when the base visitor removes it.
    /// </returns>
    public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
        => VisitWithUsings(
            node.Usings,
            () => base.VisitCompilationUnit(node.WithUsings(RewriteUsings(node.Usings))));

    /// <summary>
    /// Rewrites logical API imports and type expressions declared inside a file-scoped namespace.
    /// </summary>
    /// <param name="node">
    /// The file-scoped namespace to rewrite.
    /// </param>
    /// <returns>
    /// The rewritten namespace, or <see langword="null"/> when the base visitor removes it.
    /// </returns>
    public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        => VisitWithUsings(
            node.Usings,
            () => base.VisitFileScopedNamespaceDeclaration(node.WithUsings(RewriteUsings(node.Usings))));

    /// <summary>
    /// Rewrites logical API imports and type expressions declared inside a block-scoped namespace.
    /// </summary>
    /// <param name="node">
    /// The block-scoped namespace to rewrite.
    /// </param>
    /// <returns>
    /// The rewritten namespace, or <see langword="null"/> when the base visitor removes it.
    /// </returns>
    public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        => VisitWithUsings(
            node.Usings,
            () => base.VisitNamespaceDeclaration(node.WithUsings(RewriteUsings(node.Usings))));

    /// <summary>
    /// Replaces a fully qualified logical API type with its implementation type identity.
    /// </summary>
    /// <param name="node">
    /// The qualified name to inspect and rewrite.
    /// </param>
    /// <returns>
    /// The rewritten name, or the result produced by the base visitor when no mapping applies.
    /// </returns>
    public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
    {
        if (m_qualifiedTypeMappings.TryGetValue(node.ToString(), out string? implementationName))
            return SyntaxFactory.ParseName(implementationName).WithTriviaFrom(node);
        return base.VisitQualifiedName(node);
    }

    /// <summary>
    /// Qualifies logical API type receivers before C# namespace lookup can bind them incorrectly.
    /// </summary>
    /// <param name="node">
    /// The member-access expression whose receiver may identify an exported API type.
    /// </param>
    /// <returns>
    /// The rewritten member access, or the result produced by the base visitor when no mapping applies.
    /// </returns>
    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node.Expression is IdentifierNameSyntax identifier &&
            m_activeSimpleTypeMappings.TryGetValue(identifier.Identifier.ValueText, out string? simpleType))
        {
            return base.VisitMemberAccessExpression(node.WithExpression(
                SyntaxFactory.ParseExpression("global::" + simpleType).WithTriviaFrom(node.Expression)));
        }
        if (m_qualifiedTypeMappings.TryGetValue(node.Expression.ToString(), out string? implementationName))
        {
            return base.VisitMemberAccessExpression(node.WithExpression(
                SyntaxFactory.ParseExpression(implementationName).WithTriviaFrom(node.Expression)));
        }
        return base.VisitMemberAccessExpression(node);
    }

    private SyntaxNode? VisitWithUsings(
        SyntaxList<UsingDirectiveSyntax> usings,
        Func<SyntaxNode?> visit)
    {
        IReadOnlyDictionary<string, string> previous = m_activeSimpleTypeMappings;
        m_activeSimpleTypeMappings = CreateSimpleTypeMappings(usings, previous);
        try
        {
            return visit();
        }
        finally
        {
            m_activeSimpleTypeMappings = previous;
        }
    }

    private IReadOnlyDictionary<string, string> CreateSimpleTypeMappings(
        SyntaxList<UsingDirectiveSyntax> usings,
        IReadOnlyDictionary<string, string> inherited)
    {
        var candidates = inherited
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (UsingDirectiveSyntax usingDirective in usings)
        {
            if (usingDirective.Alias is not null ||
                usingDirective.StaticKeyword != default ||
                usingDirective.Name is null ||
                !m_typeMappings.TryGetValue(usingDirective.Name.ToString(), out ScriptApiTypeMapping[]? mappings))
            {
                continue;
            }
            foreach (ScriptApiTypeMapping mapping in mappings)
            {
                string implementation = mapping.implementationNamespace + "." + mapping.implementationName;
                if (candidates.TryGetValue(mapping.apiName, out string? existing) &&
                    !string.Equals(existing, implementation, StringComparison.Ordinal))
                {
                    ambiguous.Add(mapping.apiName);
                    continue;
                }
                candidates[mapping.apiName] = implementation;
            }
        }
        foreach (string name in ambiguous)
            candidates.Remove(name);
        return candidates;
    }

    private SyntaxList<UsingDirectiveSyntax> RewriteUsings(SyntaxList<UsingDirectiveSyntax> usings)
    {
        var result = new List<UsingDirectiveSyntax>(usings.Count);
        foreach (UsingDirectiveSyntax usingDirective in usings)
        {
            if (usingDirective.Alias is not null ||
                usingDirective.StaticKeyword != default ||
                usingDirective.Name is null ||
                !m_mappings.TryGetValue(usingDirective.Name.ToString(), out string[]? implementations) ||
                implementations.Length == 0)
            {
                result.Add(usingDirective);
                continue;
            }

            if (m_typeMappings.TryGetValue(usingDirective.Name.ToString(), out ScriptApiTypeMapping[]? aliases))
            {
                foreach (ScriptApiTypeMapping alias in aliases)
                {
                    result.Add(SyntaxFactory.UsingDirective(
                            SyntaxFactory.ParseName(
                                "global::" + alias.implementationNamespace + "." + alias.implementationName))
                        .WithAlias(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(alias.apiName))));
                }
            }
            for (int i = 0; i < implementations.Length; i++)
            {
                UsingDirectiveSyntax rewritten = usingDirective.WithName(
                    SyntaxFactory.ParseName(implementations[i]).WithTriviaFrom(usingDirective.Name));
                if (i > 0)
                    rewritten = rewritten.WithLeadingTrivia(default(SyntaxTriviaList));
                result.Add(rewritten);
            }
        }
        return SyntaxFactory.List(result);
    }
}
