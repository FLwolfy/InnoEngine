using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Editor.Scripting;

internal sealed class SerializablePropertyOrderRewriter : CSharpSyntaxRewriter
{
    private const string C_ATTRIBUTE_NAME = "SerializableProperty";
    private const string C_ATTRIBUTE_TYPE_NAME = "SerializablePropertyAttribute";
    private const string C_ORDER_PROPERTY_NAME = "order";

    private readonly Stack<int> m_nextOrders = [];

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        if (node is not TypeDeclarationSyntax)
            return base.Visit(node);

        m_nextOrders.Push(0);
        try
        {
            return base.Visit(node);
        }
        finally
        {
            _ = m_nextOrders.Pop();
        }
    }

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        return base.VisitFieldDeclaration(RewriteMember(node, node.AttributeLists));
    }

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        return base.VisitPropertyDeclaration(RewriteMember(node, node.AttributeLists));
    }

    private TMember RewriteMember<TMember>(
        TMember member,
        SyntaxList<AttributeListSyntax> attributeLists)
        where TMember : MemberDeclarationSyntax
    {
        if (m_nextOrders.Count == 0 || !TryFindSerializableAttribute(attributeLists, out AttributeSyntax attribute))
            return member;

        int order = m_nextOrders.Pop();
        m_nextOrders.Push(order + 1);
        if (HasExplicitOrder(attribute))
            return member;

        AttributeArgumentSyntax orderArgument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.NameEquals(C_ORDER_PROPERTY_NAME),
            nameColon: null,
            SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(order)));
        AttributeArgumentListSyntax argumentList = attribute.ArgumentList ?? SyntaxFactory.AttributeArgumentList();
        AttributeSyntax rewrittenAttribute = attribute.WithArgumentList(argumentList.AddArguments(orderArgument));
        return member.ReplaceNode(attribute, rewrittenAttribute);
    }

    private static bool TryFindSerializableAttribute(
        SyntaxList<AttributeListSyntax> attributeLists,
        out AttributeSyntax result)
    {
        foreach (AttributeSyntax attribute in attributeLists.SelectMany(static list => list.Attributes))
        {
            string name = GetRightmostName(attribute.Name);
            if (name is C_ATTRIBUTE_NAME or C_ATTRIBUTE_TYPE_NAME)
            {
                result = attribute;
                return true;
            }
        }

        result = null!;
        return false;
    }

    private static bool HasExplicitOrder(AttributeSyntax attribute)
    {
        return attribute.ArgumentList?.Arguments.Any(static argument =>
            string.Equals(argument.NameEquals?.Name.Identifier.ValueText, C_ORDER_PROPERTY_NAME,
                System.StringComparison.Ordinal)) == true;
    }

    private static string GetRightmostName(NameSyntax name)
    {
        return name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => GetRightmostName(qualified.Right),
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => name.ToString()
        };
    }
}
