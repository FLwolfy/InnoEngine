using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Inno.Assets.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Inno.Scripting.Compiler;

/// <summary>
/// Gives imported C# sample scripts distinct type identities before their scene assets are remapped.
/// </summary>
[AssetSampleSourceRewriter("inno.scripting.csharp-sample")]
public sealed class CSharpSampleSourceRewriter : IAssetSampleSourceRewriter
{
    /// <summary>
    /// Rewrites explicit stable type identities in cloned C# scripts and registers their asset mappings.
    /// </summary>
    /// <param name="context">
    /// The private sample import stage and its identity map.
    /// </param>
    public void Transform(AssetSampleTransformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (string path in Directory.GetFiles(context.stagedRoot, "*.cs", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(context.stagedRoot, path).Replace('\\', '/');
            if (!context.TryGetSourceIdentity(relative, out Guid oldSourceId, out Guid newSourceId))
                throw new InvalidDataException($"Sample C# source '{relative}' has no asset identity metadata.");
            context.MapType(ScriptTypeIdentity.CreateCanonical(oldSourceId),
                ScriptTypeIdentity.CreateCanonical(newSourceId));

            byte[] bytes = File.ReadAllBytes(path);
            bool hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
            string source = File.ReadAllText(path, Encoding.UTF8);
            SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
            AttributeSyntax[] attributes = root.DescendantNodes().OfType<AttributeSyntax>()
                .Where(static attribute => IsStableTypeId(attribute.Name.ToString())).ToArray();
            if (attributes.Length == 0)
                continue;
            SyntaxNode rewritten = root.ReplaceNodes(attributes, (original, _) =>
            {
                AttributeSyntax attribute = (AttributeSyntax)original;
                if (attribute.ArgumentList?.Arguments is not { Count: 1 } arguments
                    || arguments[0].Expression is not LiteralExpressionSyntax literal
                    || !literal.IsKind(SyntaxKind.StringLiteralExpression)
                    || !Guid.TryParse(literal.Token.ValueText, out Guid oldId))
                {
                    throw new InvalidDataException(
                        $"Sample C# source '{relative}' has a StableTypeId that cannot be cloned safely.");
                }
                Guid newId = context.identityMap.TryGetValue(oldId, out Guid mapped)
                    ? mapped : Guid.NewGuid();
                context.MapType(oldId, newId);
                LiteralExpressionSyntax replacement = SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(newId.ToString("D")))
                    .WithTriviaFrom(literal);
                return attribute.WithArgumentList(attribute.ArgumentList.WithArguments(
                    SyntaxFactory.SingletonSeparatedList(arguments[0].WithExpression(replacement))));
            });
            File.WriteAllText(path, rewritten.ToFullString(), new UTF8Encoding(hasBom));
        }
    }

    private static bool IsStableTypeId(string name)
        => name.Split('.').Last() is "StableTypeId" or "StableTypeIdAttribute";
}
