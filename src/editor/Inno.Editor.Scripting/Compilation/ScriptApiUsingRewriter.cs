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
    private readonly HashSet<string> m_additionalGlobalUsings = new(StringComparer.Ordinal);

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

    internal IReadOnlyCollection<string> additionalGlobalUsings => m_additionalGlobalUsings;

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Alias is not null || node.StaticKeyword != default || node.Name is null ||
            !m_mappings.TryGetValue(node.Name.ToString(), out string[]? implementations) ||
            implementations.Length == 0)
        {
            return base.VisitUsingDirective(node);
        }

        for (int i = 1; i < implementations.Length; i++)
            m_additionalGlobalUsings.Add(implementations[i]);
        return node.WithName(SyntaxFactory.ParseName(implementations[0]).WithTriviaFrom(node.Name));
    }
}
