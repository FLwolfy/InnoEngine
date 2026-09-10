using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Inno.Scripting.Api;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Scripting.Compiler;

/// <summary>Removes explicitly declared compile-time annotation types before Player reference binding.</summary>
internal sealed class ScriptAuthoringAnnotationRewriter(
    SemanticModel semanticModel,
    HashSet<string> annotationTypes,
    HashSet<string> annotationNamespaces) : CSharpSyntaxRewriter
{
    internal static SyntaxTree[] Erase(CSharpCompilation compilation, ScriptApiProfile api, CancellationToken cancellationToken)
    {
        Type[] types = api.exports.SelectMany(export => export.assembly
                .GetCustomAttributes(typeof(ScriptingApiExportAttribute), false)
                .Cast<ScriptingApiExportAttribute>())
            .Where(export => export.scope == ScriptingApiScope.Authoring)
            .Select(export => export.type).Distinct().ToArray();
        var names = types.Select(type => type.FullName!).ToHashSet(StringComparer.Ordinal);
        var namespaces = types.Select(type => type.Namespace!).ToHashSet(StringComparer.Ordinal);
        return compilation.SyntaxTrees.Select(tree =>
        {
            var rewriter = new ScriptAuthoringAnnotationRewriter(compilation.GetSemanticModel(tree), names, namespaces);
            return tree.WithRootAndOptions(rewriter.Visit(tree.GetRoot(cancellationToken))!, tree.Options);
        }).ToArray();
    }

    /// <inheritdoc />
    public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
    {
        AttributeSyntax[] retained = node.Attributes.Where(attribute =>
            !IsAnnotation((semanticModel.GetSymbolInfo(attribute).Symbol as IMethodSymbol)?.ContainingType)).ToArray();
        return retained.Length == 0 ? null : node.WithAttributes(SyntaxFactory.SeparatedList(retained));
    }

    /// <inheritdoc />
    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        => IsAnnotation(semanticModel.GetDeclaredSymbol(node)) ? null : base.VisitClassDeclaration(node);

    /// <inheritdoc />
    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        ISymbol? symbol = node.Name is null ? null : semanticModel.GetSymbolInfo(node.Name).Symbol;
        return symbol is INamedTypeSymbol type && IsAnnotation(type)
               || symbol is INamespaceSymbol ns && annotationNamespaces.Contains(ns.ToDisplayString())
            ? null : base.VisitUsingDirective(node);
    }

    private bool IsAnnotation(INamedTypeSymbol? type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (annotationTypes.Contains(current.ToDisplayString()))
                return true;
        return false;
    }
}
