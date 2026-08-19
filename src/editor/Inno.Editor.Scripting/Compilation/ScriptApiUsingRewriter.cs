using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Editor.Scripting;

internal sealed class ScriptApiUsingRewriter : CSharpSyntaxRewriter
{
    private readonly IReadOnlyDictionary<string, string[]> m_mappings;

    internal ScriptApiUsingRewriter(IReadOnlyList<ScriptApiNamespaceMapping> mappings)
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
    }

    public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
        => base.VisitCompilationUnit(node.WithUsings(RewriteUsings(node.Usings)));

    public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        => base.VisitFileScopedNamespaceDeclaration(node.WithUsings(RewriteUsings(node.Usings)));

    public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        => base.VisitNamespaceDeclaration(node.WithUsings(RewriteUsings(node.Usings)));

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
