using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Scripting.Compiler;

// Preserve bound user static members before logical API imports become implementation namespaces.
// For example, a using-static Input() function must not become a reference to the Inno.Input namespace.
internal sealed class ScriptStaticMemberRewriter(SemanticModel model) : CSharpSyntaxRewriter
{
    /// <summary>
    /// Visits identifier name in deterministic order using the supplied visitor.
    /// </summary>
    /// <param name="node">
    /// The node consumed by visit identifier name; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated syntax node? that represents the completed operation.
    /// </returns>
public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        => Qualify(node) ?? base.VisitIdentifierName(node);

    /// <summary>
    /// Visits generic name in deterministic order using the supplied visitor.
    /// </summary>
    /// <param name="node">
    /// The node consumed by visit generic name; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// The validated syntax node? that represents the completed operation.
    /// </returns>
public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        => Qualify(node) ?? base.VisitGenericName(node);

    private SyntaxNode? Qualify(SimpleNameSyntax node)
    {
        if (node.Parent is MemberAccessExpressionSyntax member && member.Name == node
            || node.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax) return null;
        if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol { IsStatic: true, MethodKind: MethodKind.Ordinary } method
            || !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, model.Compilation.Assembly)) return null;
        return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParseExpression(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            node.WithoutTrivia()).WithTriviaFrom(node);
    }
}
